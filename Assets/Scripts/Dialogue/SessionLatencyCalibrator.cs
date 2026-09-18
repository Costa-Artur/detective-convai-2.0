using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Detective.Dialogue
{
    // Mede a latencia real DESTA sessao de jogo (modelo + rede do jogador
    // especifico) durante a tela de carregamento, e depois fornece delays
    // aleatorios realistas para as falas dos NPCs roteirizados (Yarn).
    //
    // Por que por sessao, e nao so o arquivo historico do LatencySampleStore:
    // aquele arquivo reflete a rede da maquina de DESENVOLVIMENTO. Cada
    // participante do teste tem uma conexao diferente - se o delay artificial
    // dos NPCs Yarn nao acompanhar a rede real do jogador, o NPC dinamico
    // (que sofre a latencia de verdade) fica distinguivel por eliminacao,
    // que e exatamente o confundidor que o §5 do mapa quer eliminar.
    //
    // Sobrevive a troca de cena (DontDestroyOnLoad) para que a cena de
    // interrogatorio use a calibragem feita no menu.
    public class SessionLatencyCalibrator : MonoBehaviour
    {
        public static SessionLatencyCalibrator Instance { get; private set; }

        [Header("Configuração")]
        public AzureOpenAIConfig config;

        [Tooltip("Quantas chamadas reais disparar durante o carregamento para calibrar.")]
        [Range(1, 10)]
        public int probeCount = 4;

        [Tooltip("Faixa (segundos) usada se a calibragem falhar completamente - ex: jogador " +
                 "offline, chave invalida. Evita delay zero, que denunciaria os NPCs Yarn.")]
        public Vector2 fallbackRangeSeconds = new Vector2(1.2f, 3.0f);

        [Tooltip("Também acumula as medições no arquivo histórico (Logs/), para análise " +
                 "posterior no TCC. Não afeta o delay usado em runtime.")]
        public bool alsoPersistToHistory = true;

        private readonly List<float> _sessionSamplesMs = new List<float>();

        public bool IsCalibrated => _sessionSamplesMs.Count > 0;
        public int SampleCount => _sessionSamplesMs.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroi apenas ESTE componente, nunca o GameObject inteiro:
                // se alguem anexar o calibrador a um objeto que carrega outras
                // coisas (ex: o GameController, que tem meia duzia de sistemas
                // do jogo), destruir o objeto levaria tudo junto - e em
                // silencio, porque duplicata e um caso "normal".
                Debug.LogWarning($"[SessionLatencyCalibrator] Já existe um calibrador na cena " +
                                 $"('{Instance.gameObject.name}'). O duplicado em '{gameObject.name}' " +
                                 "foi removido. Mantenha apenas um, na cena do menu.");
                Destroy(this);
                return;
            }

            Instance = this;

            // DontDestroyOnLoad só funciona em objetos de raiz - por isso o guia
            // pede um GameObject vazio e dedicado, não um filho de outro objeto.
            if (transform.parent != null)
            {
                Debug.LogWarning($"[SessionLatencyCalibrator] '{gameObject.name}' é filho de " +
                                 $"'{transform.parent.name}'. Para sobreviver à troca de cena ele " +
                                 "precisa estar na raiz da Hierarchy.");
            }

            DontDestroyOnLoad(gameObject);
        }

        // Dispara probeCount chamadas reais e guarda as latencias desta sessao.
        // Chamado durante a tela de carregamento (ver ASyncLoader).
        public async Task Calibrate()
        {
            _sessionSamplesMs.Clear();

            if (config == null)
            {
                Debug.LogWarning("[SessionLatencyCalibrator] 'config' não atribuído - usando faixa de fallback.");
                return;
            }

            if (!AzureApiKeyLoader.TryLoad(out string apiKey))
            {
                Debug.LogWarning("[SessionLatencyCalibrator] Sem chave de API - usando faixa de fallback.");
                return;
            }

            var client = new AzureOpenAIDialogueClient(config, apiKey);
            string tag = LatencySampleStore.BuildTag(config);

            client.OnRequestCompleted += (ms, success) =>
            {
                if (!success) return;

                _sessionSamplesMs.Add(ms);
                if (alsoPersistToHistory)
                    LatencySampleStore.AppendSample(ms, tag);
            };

            // Prompt minimo: so queremos o tempo de ida e volta, nao o conteudo.
            string systemPrompt =
                $"Responda em até 50 caracteres, com exatamente {config.optionCount} opções, " +
                "e defina \"encerrar\": true.";
            var history = new List<ChatMessage> { new ChatMessage("user", "Olá.") };

            for (int i = 0; i < probeCount; i++)
            {
                try
                {
                    await client.GenerateTurnAsync(systemPrompt, history);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[SessionLatencyCalibrator] Sondagem {i + 1}/{probeCount} falhou: {ex.Message}");
                }
            }

            if (IsCalibrated)
            {
                Debug.Log($"[SessionLatencyCalibrator] Calibrado com {_sessionSamplesMs.Count}/{probeCount} amostras. " +
                          $"Média: {_sessionSamplesMs.Average():F0}ms · " +
                          $"Min: {_sessionSamplesMs.Min():F0}ms · Max: {_sessionSamplesMs.Max():F0}ms");
            }
            else
            {
                Debug.LogWarning("[SessionLatencyCalibrator] Nenhuma sondagem teve sucesso - usando faixa de fallback.");
            }
        }

        // Delay a aplicar antes de exibir a fala de um NPC roteirizado (Yarn),
        // para que ele nao responda instantaneamente enquanto o NPC dinamico
        // sofre latencia real. Ver §5 do mapa.
        //
        // Prioridade: amostras desta sessao (rede real do jogador) > historico
        // acumulado (rede do desenvolvedor) > faixa fixa de fallback.
        public float GetRandomDelaySeconds()
        {
            if (IsCalibrated)
                return _sessionSamplesMs[Random.Range(0, _sessionSamplesMs.Count)] / 1000f;

            float? historical = LatencySampleStore.GetRandomDelaySeconds(LatencySampleStore.BuildTag(config));
            if (historical.HasValue)
                return historical.Value;

            return Random.Range(fallbackRangeSeconds.x, fallbackRangeSeconds.y);
        }
    }
}
