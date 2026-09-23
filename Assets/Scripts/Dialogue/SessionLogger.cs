using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Detective.Dialogue
{
    // Log de sessao (RF10, RNF09): registra em arquivo tudo o que acontece em
    // uma partida - NPC sorteado como dinamico, cartas de cada um, solucao do
    // crime, turnos de dialogo das duas origens, requisicoes e respostas da
    // IA, revelacoes de carta, palpites e acusacao final.
    //
    // Dois arquivos por partida, com o mesmo nome:
    //   .jsonl - um evento JSON por linha, para analise (inclui o prompt completo)
    //   .txt   - o mesmo conteudo em texto legivel, sem os prompts
    // No Editor ficam em Logs/sessoes/ (fora do git); num build, em
    // Application.persistentDataPath/sessoes/.
    //
    // Nao precisa de objeto na cena: a sessao comeca sozinha no primeiro
    // evento de cada carregamento da cena de jogo. O arquivo e escrito evento
    // a evento, entao sobrevive a um fechamento abrupto do jogo. Nada aqui
    // aparece para o jogador - so o caminho do arquivo, no Console.
    public static class SessionLogger
    {
        private static Scene _cena;
        private static string _sessionId;
        private static string _jsonlPath;
        private static string _txtPath;
        private static DateTime _inicio;
        private static List<(string nome, string tipo)> _baralho = new List<(string, string)>();

        public static string SessionId
        {
            get { EnsureSession(); return _sessionId; }
        }

        private static string Pasta => Application.isEditor
            ? Path.Combine(Application.dataPath, "..", "Logs", "sessoes")
            : Path.Combine(Application.persistentDataPath, "sessoes");

        private static void EnsureSession()
        {
            // Cada carregamento da cena de jogo e uma cena nova (outro handle),
            // entao voltar ao menu e jogar de novo abre outra sessao.
            Scene atual = SceneManager.GetActiveScene();
            if (_jsonlPath != null && atual == _cena)
                return;

            _cena = atual;
            _inicio = DateTime.Now;
            _sessionId = _inicio.ToString("yyyyMMdd-HHmmss") + "-" +
                         Guid.NewGuid().ToString("N").Substring(0, 4);
            _baralho = new List<(string, string)>();

            try
            {
                Directory.CreateDirectory(Pasta);
                _jsonlPath = Path.Combine(Pasta, $"sessao_{_sessionId}.jsonl");
                _txtPath = Path.Combine(Pasta, $"sessao_{_sessionId}.txt");
                Debug.Log($"[SessionLogger] Log desta partida: {Path.GetFullPath(_txtPath)}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SessionLogger] Nao foi possivel criar a pasta de logs: {ex.Message}");
            }

            Log("sessao_inicio",
                ("sessao", _sessionId),
                ("cena", SceneManager.GetActiveScene().name),
                ("versao_unity", Application.unityVersion),
                ("plataforma", Application.platform.ToString()));
        }

        // Registra um evento. Campos cujo nome comeca com "prompt" vao so para
        // o .jsonl (sao longos demais para leitura).
        public static void Log(string evento, params (string chave, object valor)[] campos)
        {
            try
            {
                EnsureSession();
                if (_jsonlPath == null)
                    return;

                DateTime agora = DateTime.Now;
                double segundos = (agora - _inicio).TotalSeconds;

                var json = new StringBuilder();
                json.Append("{\"t\":\"").Append(agora.ToString("o")).Append("\",");
                json.Append("\"s\":").Append(segundos.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                json.Append("\"evento\":").Append(JsonString(evento));

                var txt = new StringBuilder();
                txt.Append('[').Append(agora.ToString("HH:mm:ss.fff")).Append("] ").Append(evento);

                foreach (var (chave, valor) in campos)
                {
                    json.Append(',').Append(JsonString(chave)).Append(':').Append(JsonValue(valor));
                    if (!chave.StartsWith("prompt", StringComparison.Ordinal))
                        txt.Append("\n    ").Append(chave).Append(": ").Append(TextValue(valor));
                }
                json.Append('}');

                File.AppendAllText(_jsonlPath, json + "\n", Encoding.UTF8);
                File.AppendAllText(_txtPath, txt + "\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // O log nunca pode derrubar a sessao de teste.
                Debug.LogWarning($"[SessionLogger] Falha ao registrar '{evento}': {ex.Message}");
            }
        }

        // ---- Ajudantes de dominio ----

        // Nome exibido do NPC (o mesmo da interface). Usa o do roteirizado,
        // que e a referencia; cai no nome do GameObject.
        public static string NomeNpc(Component c)
        {
            if (c == null) return "?";
            var yarn = c.GetComponent<YarnDialogueSource>();
            if (yarn != null && !string.IsNullOrWhiteSpace(yarn.speakerName)) return yarn.speakerName;
            var dyn = c.GetComponent<DynamicDialogueSource>();
            if (dyn != null && !string.IsNullOrWhiteSpace(dyn.speakerName)) return dyn.speakerName;
            return c.gameObject.name;
        }

        public static string Carta(Clue c) => c == null ? "(nenhuma)" : $"{c.evidenceName} [{c.type}]";

        public static string[] Cartas(IEnumerable<Clue> cartas) =>
            cartas == null ? Array.Empty<string>() : cartas.Where(c => c != null).Select(Carta).ToArray();

        // Guarda o nome e o tipo de todas as cartas do jogo, usados na
        // verificacao das falas da IA.
        public static void RegistrarBaralho(IEnumerable<Clue> baralho)
        {
            EnsureSession();
            _baralho = baralho.Where(c => c != null).Select(c => (c.evidenceName, c.type)).ToList();
        }

        // Confere se a fala da IA combina com a carta que o jogo de fato
        // revelou. Registra so quando ha algo a apontar. Nomes de suspeitos
        // nao sao verificados: coincidem com os nomes dos convidados, que os
        // personagens citam o tempo todo sem estar revelando carta nenhuma.
        // Cartas cujo nome aparece em textoDaPersona (ex.: os comodos do
        // alibi) tambem sao ignoradas - citar o alibi nao e revelar carta.
        public static void VerificarCartasNaFala(string npc, string fala, string revelarPista,
                                                 List<Clue> cartasDoNpc, Clue cartaRevelada,
                                                 string textoDaPersona = null)
        {
            if (string.IsNullOrEmpty(fala) || _baralho.Count == 0)
                return;

            string falaNorm = Normalizar(fala);
            string personaNorm = string.IsNullOrEmpty(textoDaPersona) ? "" : Normalizar(textoDaPersona);
            var citadas = _baralho
                .Where(c => c.tipo != "suspeito" && falaNorm.Contains(Normalizar(c.nome)))
                .Where(c => personaNorm.Length == 0 || !personaNorm.Contains(Normalizar(c.nome)))
                .ToList();

            var possuidas = new HashSet<string>(
                (cartasDoNpc ?? new List<Clue>()).Where(c => c != null).Select(c => c.evidenceName));

            var problemas = new List<string>();

            foreach (var c in citadas.Where(c => !possuidas.Contains(c.nome)))
                problemas.Add($"a fala cita '{c.nome}' [{c.tipo}], que este NPC NAO possui");

            // Os roteirizados nao podem nomear a carta que entregam (ela e
            // sorteada); o dinamico tambem nao deve, ou se diferencia.
            if (cartaRevelada != null && falaNorm.Contains(Normalizar(cartaRevelada.evidenceName)))
                problemas.Add($"a fala nomeia a carta entregue '{cartaRevelada.evidenceName}' " +
                              "(os roteirizados nunca nomeiam)");

            if (cartaRevelada != null)
            {
                bool revelouCitada = citadas.Any(c => c.nome == cartaRevelada.evidenceName);
                var outrasDoMesmoTipo = citadas.Where(c => c.tipo == cartaRevelada.type &&
                                                           c.nome != cartaRevelada.evidenceName).ToList();
                if (!revelouCitada && outrasDoMesmoTipo.Count > 0)
                    problemas.Add($"a fala cita '{outrasDoMesmoTipo[0].nome}', mas o jogo revelou " +
                                  $"'{cartaRevelada.evidenceName}'");
            }
            else if (string.IsNullOrEmpty(revelarPista))
            {
                foreach (var c in citadas.Where(c => possuidas.Contains(c.nome)))
                    problemas.Add($"a fala cita '{c.nome}' [{c.tipo}], que o NPC possui, sem revelar_pista " +
                                  "(nenhum painel de carta foi aberto)");
            }

            if (problemas.Count == 0)
                return;

            Log("ia_verificacao_cartas",
                ("npc", npc),
                ("fala", fala),
                ("revelar_pista", revelarPista ?? ""),
                ("carta_revelada", cartaRevelada == null ? "(nenhuma)" : Carta(cartaRevelada)),
                ("cartas_do_npc", Cartas(cartasDoNpc)),
                ("problemas", problemas.ToArray()));
        }

        private static string Normalizar(string s)
        {
            string decomposto = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposto.Length);
            foreach (char ch in decomposto)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return sb.ToString();
        }

        // ---- Serializacao ----

        private static string JsonValue(object v)
        {
            switch (v)
            {
                case null: return "null";
                case string s: return JsonString(s);
                case bool b: return b ? "true" : "false";
                case int or long or short: return Convert.ToString(v, CultureInfo.InvariantCulture);
                case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                case double d: return d.ToString("0.###", CultureInfo.InvariantCulture);
                case IEnumerable e:
                    var partes = new List<string>();
                    foreach (object item in e) partes.Add(JsonValue(item));
                    return "[" + string.Join(",", partes) + "]";
                default: return JsonString(v.ToString());
            }
        }

        private static string TextValue(object v)
        {
            switch (v)
            {
                case null: return "(nulo)";
                case string s: return s.Replace("\n", "\n      ");
                case IEnumerable e and not string:
                    var partes = new List<string>();
                    int i = 0;
                    foreach (object item in e) partes.Add($"\n      [{i++}] {TextValue(item)}");
                    return partes.Count == 0 ? "(vazio)" : string.Concat(partes);
                case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                case double d: return d.ToString("0.###", CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        private static string JsonString(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
