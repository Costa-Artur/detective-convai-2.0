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

        // Latencias das falas REAIS do NPC dinamico nesta partida. Sao a
        // melhor referencia possivel: mesmo prompt, mesma rede, mesmo momento.
        private readonly List<float> _liveSamplesMs = new List<float>();

        public bool IsCalibrated => _sessionSamplesMs.Count > 0 || _liveSamplesMs.Count > 0;
        public int SampleCount => _sessionSamplesMs.Count + _liveSamplesMs.Count;

        // Chamado pelo DynamicNPCController a cada fala gerada com sucesso.
        public void AddLiveSample(float latencyMs)
        {
            if (latencyMs > 0f)
                _liveSamplesMs.Add(latencyMs);
        }

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

            // A sondagem precisa custar o mesmo que uma fala real: prompt do
            // mesmo tamanho (cenario + persona + regras + roteiro do turno) e
            // resposta completa. Um prompt minimo mede so a ida e volta da rede
            // e subestima a latencia - os roteirizados responderiam mais rapido
            // que o NPC dinamico, que e justamente o que se quer evitar.
            string systemPrompt = BuildProbePrompt();
            var history = new List<ChatMessage>
            {
                new ChatMessage("assistant", "Pois nao, detetive. Pergunte o que precisar."),
                new ChatMessage("user", "O que a senhora fez durante a festa?")
            };
            var probeCards = new List<string> { "Faca", "Biblioteca", "Senhor Verde" };

            for (int i = 0; i < probeCount; i++)
            {
                try
                {
                    await client.GenerateTurnAsync(systemPrompt, history, probeCards);
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
        // Prompt de sondagem com o mesmo formato e tamanho do prompt real do
        // DynamicNPCController (mesmas secoes; persona generica).
        private string BuildProbePrompt()
        {
            return
                "Voce interpreta UM personagem sendo interrogado por um detetive em um jogo de misterio. " +
                "Regras invioláveis do seu papel:\n" +
                "- Fale SEMPRE em primeira pessoa, como o personagem. Voce nao e narrador.\n" +
                "- Nunca descreva a cena, o ambiente, nem as acoes de outras pessoas em terceira pessoa.\n" +
                "- As opcoes que voce devolve sao FALAS DO DETETIVE dirigidas a voce.\n\n" +
                config.sharedSceneContext + "\n\n" +
                "Voce e: uma convidada da festa.\n\n" +
                "Seu personagem:\n" +
                "Voce e uma convidada antiga da familia Vargas, discreta e observadora. Conhecia os negocios " +
                "dele de longe e nunca gostou do modo como ele tratava as pessoas proximas.\n\n" +
                "Seu alibi (fatos fixos):\n" +
                "- Durante o periodo do crime, voce esteve primeiro na Biblioteca, lendo sozinha.\n" +
                "- Depois, foi ao Salao de Festas, onde viu outro convidado por parte do tempo.\n" +
                "- Brecha: na Biblioteca voce ficou sozinha por cerca de seis minutos.\n\n" +
                "Como voce fala:\n" +
                "- Com calma e frases medidas, sem floreios.\n" +
                "- Voce desvia de perguntas pessoais com cortesia.\n\n" +
                "Comportamento:\n" +
                "- Voce admite ter segredos, mas nunca os detalha.\n" +
                "- Conte sempre a mesma versao do seu alibi.\n" +
                "- Nunca cite horarios exatos.\n\n" +
                "Cartas de pista que voce possui:\n- Faca (arma do crime)\n- Biblioteca (local)\n" +
                "- Senhor Verde (suspeito)\n\n" +
                "Regras de estilo OBRIGATORIAS:\n" +
                $"- Cada fala sua tem no maximo {config.maxChars} caracteres e no maximo duas frases.\n" +
                $"- Ofereca exatamente {config.optionCount} opcoes curtas do detetive.\n" +
                "- Nao use aspas. Nunca saia do personagem. Nao repita frases de turnos anteriores.\n" +
                "- Nunca diga o nome de uma carta. Nunca afirme quem e o culpado.\n\n" +
                "Campos da resposta: papeis_opcoes, carta_alvo, revelar_pista, encerrar.\n\n" +
                // Turno 2: custo intermediario. O turno do indicio (3) e o que
                // mais exige raciocinio e superestimaria o atraso dos
                // roteirizados (sessao de 20 set: sondagem 4,1s x real 1,8-2,6s).
                "ROTEIRO DESTE TURNO (turno 2 de no maximo 5):\n" +
                "Responda ao que o detetive perguntou, sem entregar pistas. Opcoes: duas que aprofundam " +
                "o assunto (papel aprofundar) e uma em que o detetive agradece e encerra a conversa " +
                "(papel saida). carta_alvo = \"\". encerrar = false.";
        }

        public float GetRandomDelaySeconds()
        {
            // Depois que o jogador ja conversou com o NPC dinamico, as
            // latencias reais dele passam a ser a referencia.
            if (_liveSamplesMs.Count >= 2)
                return _liveSamplesMs[Random.Range(0, _liveSamplesMs.Count)] / 1000f;

            if (_sessionSamplesMs.Count > 0 || _liveSamplesMs.Count > 0)
            {
                int total = _sessionSamplesMs.Count + _liveSamplesMs.Count;
                int k = Random.Range(0, total);
                float ms = k < _sessionSamplesMs.Count ? _sessionSamplesMs[k] : _liveSamplesMs[k - _sessionSamplesMs.Count];
                return ms / 1000f;
            }

            float? historical = LatencySampleStore.GetRandomDelaySeconds(LatencySampleStore.BuildTag(config));
            if (historical.HasValue)
                return historical.Value;

            return Random.Range(fallbackRangeSeconds.x, fallbackRangeSeconds.y);
        }
    }
}
