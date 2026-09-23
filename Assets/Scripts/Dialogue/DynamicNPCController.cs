using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Detective.Dialogue
{
    // Controla a conversa com o NPC dinamico: mantem historico e turnIndex,
    // chama o Azure OpenAI a cada turno e aplica o cap de profundidade (maxTurns).
    // Ver docs/arquitetura-npc-dinamico.md secao 3.1.
    //
    // O NPC dinamico segue o MESMO roteiro de turnos do esqueleto dos NPCs
    // roteirizados (diario, secao 08): abertura com tres temas -> aprofundar
    // (com saida) -> indicio de uma carta -> pedido ou pressao -> entrega da
    // carta, que encerra a conversa. O modelo escreve o texto; o codigo decide
    // a estrutura. Cada opcao gerada vem com um papel (OptionRoles), e o papel
    // escolhido pelo jogador define o roteiro do turno seguinte. As regras que
    // um jogador perceberia se falhassem - encerrar depois de entregar a
    // carta, de sair ou de acusar; entregar a carta pedida; nunca entregar
    // sem pedido - sao impostas aqui, depois da resposta, e nao so pedidas no
    // prompt. Sem isso o NPC dinamico se denunciaria por se comportar
    // diferente dos roteirizados.
    public class DynamicNPCController : MonoBehaviour
    {
        [Header("Configuracao")]
        public AzureOpenAIConfig config;

        [Tooltip("Cache local usado se a chamada ao Azure OpenAI falhar (risco 4.7.3 do TCC: " +
                 "'Problemas tecnicos durante a integracao com motores de IA').")]
        public FallbackDialoguePool fallbackPool;

        [Header("Persona do personagem (base fatual - ver doc secao 2.2)")]
        [TextArea(5, 15)]
        public string personaBackstory;

        [Tooltip("Cartas extras injetadas no prompt, escritas à mão. As cartas REAIS da partida " +
                 "são lidas automaticamente do LocalInventory deste mesmo NPC - esta lista é " +
                 "somada àquelas, e serve principalmente para testes (ex: a pista-isca do " +
                 "DynamicNPCContextTest).")]
        public List<string> clueCardNames = new List<string>();

        private AzureOpenAIDialogueClient _client;
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private int _turnIndex;
        private int _consecutiveFailures;
        private float _lastLatencyMs = -1f;

        // Papel da opcao que o jogador escolheu no turno anterior (null na abertura).
        private string _chosenRole;

        // Carta indicada no turno em que apareceu a opcao "pedido_carta" - e a
        // que sera entregue se o jogador pedir ou insistir.
        private string _pendingCard;

        // Muda a cada conversa iniciada; respostas de conversas anteriores que
        // chegam atrasadas sao descartadas em vez de poluir o historico.
        private int _conversationId;

        // Turnos ja gerados nesta partida, indexados pelo caminho de opcoes
        // que levou a eles (a abertura e o caminho vazio). Ao voltar a este
        // NPC, o mesmo caminho repete as mesmas falas, como numa arvore .yarn:
        // antes cada visita gerava tudo de novo, e so o NPC dinamico mudava de
        // fala - o que o denunciava. Caminho novo = ramo novo, gerado na hora.
        private class CachedTurn
        {
            public DynamicTurnResult result;
            public string pendingCardAfter; // _pendingCard depois do turno
        }
        private readonly Dictionary<string, CachedTurn> _turnCache = new Dictionary<string, CachedTurn>();
        private string _pathKey = "";
        private const char PathSeparator = '\u001F';

        // true quando o ultimo turno veio do cache (sem chamada de rede).
        public bool LastTurnFromCache { get; private set; }

        // Para testes que precisam de respostas novas do modelo a cada execucao.
        public void ClearTurnCache() => _turnCache.Clear();

        public bool IsBusy { get; private set; }
        public int TurnIndex => _turnIndex;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError($"[DynamicNPCController] Campo 'config' não atribuído em '{gameObject.name}'. " +
                                "Arraste o asset AzureOpenAIConfig no Inspector.");
                enabled = false;
                return;
            }

            if (!AzureApiKeyLoader.TryLoad(out string apiKey))
            {
                enabled = false; // sem chave, este NPC nao funciona - evita null ref em runtime
                return;
            }

            _client = new AzureOpenAIDialogueClient(config, apiKey);
            _client.OnRequestCompleted += (ms, success) =>
            {
                _lastLatencyMs = ms;
                // Cada fala real calibra o atraso artificial dos roteirizados.
                if (success && SessionLatencyCalibrator.Instance != null)
                    SessionLatencyCalibrator.Instance.AddLiveSample(ms);
            };
        }

        // Inicia a conversa (equivalente ao "Inicio" de um no .yarn).
        public async Task<DynamicTurnResult> StartConversation()
        {
            _history.Clear();
            _turnIndex = 0;
            _chosenRole = null;
            _pendingCard = null;
            _conversationId++;
            _pathKey = "";
            return await RequestNextTurn();
        }

        // Avanca a conversa a partir da opcao que o jogador escolheu. O papel
        // (OptionRoles) vem do proprio turno gerado; sem ele (testes), o turno
        // seguinte e tratado como um aprofundamento.
        public async Task<DynamicTurnResult> ChooseOption(string chosenOptionText, string chosenRole = null)
        {
            _history.Add(new ChatMessage("user", chosenOptionText));
            _turnIndex++;
            _chosenRole = chosenRole;
            _pathKey += PathSeparator + chosenOptionText;
            return await RequestNextTurn();
        }

        private async Task<DynamicTurnResult> RequestNextTurn()
        {
            LastTurnFromCache = false;
            if (_turnCache.TryGetValue(_pathKey, out CachedTurn cached))
                return ReplayCachedTurn(cached);

            IsBusy = true;
            try
            {
                if (config == null)
                    throw new System.Exception("'config' não atribuído no Inspector.");
                if (_client == null)
                    throw new System.Exception(
                        "cliente não inicializado (Awake falhou - provavelmente chave de API ausente ou config nula).");

                List<string> cardNames = CollectClueCardNames();
                string systemPrompt = BuildSystemPrompt();
                int conversation = _conversationId;
                _lastLatencyMs = -1f;
                LogRequest(systemPrompt);
                DynamicTurnResult result = await _client.GenerateTurnAsync(systemPrompt, _history, cardNames);

                _consecutiveFailures = 0;
                LogResponse(result);

                if (conversation != _conversationId)
                {
                    SessionLogger.Log("ia_resposta_descartada",
                        ("npc", SessionLogger.NomeNpc(this)),
                        ("motivo", "outra conversa comecou enquanto esta resposta vinha"));
                    return result;
                }

                EnforceSkeleton(result, cardNames);
                ShuffleOptions(result);
                _history.Add(new ChatMessage("assistant", result.fala));
                // So turnos gerados pelo modelo entram no cache. Um fallback
                // (falha de rede) nao, para a proxima visita tentar de novo.
                _turnCache[_pathKey] = new CachedTurn { result = result, pendingCardAfter = _pendingCard };
                return result;
            }
            catch (System.Exception ex)
            {
                // Contingencia do TCC (4.7.3): falha de integracao com o motor de IA
                // nao deve travar a sessao de teste - cai pro cache local.
                Debug.LogWarning($"[DynamicNPCController] Falha ao gerar turno, usando fallback. {ex.Message}");

                _consecutiveFailures++;
                bool forceClose = _consecutiveFailures >= 2; // 2a falha seguida: encerra educadamente

                if (fallbackPool == null)
                {
                    Debug.LogError("[DynamicNPCController] fallbackPool nao atribuido - sem cache local para contingencia.");
                    throw;
                }

                DynamicTurnResult fallback = fallbackPool.BuildFallbackTurn(forceClose);
                _history.Add(new ChatMessage("assistant", fallback.fala));
                SessionLogger.Log("ia_falha_fallback",
                    ("npc", SessionLogger.NomeNpc(this)),
                    ("turno", _turnIndex + 1),
                    ("erro", ex.Message),
                    ("resposta_bruta", _client?.LastRawContent),
                    ("latencia_ms", _lastLatencyMs),
                    ("fala_fallback", fallback.fala),
                    ("encerramento_forcado", forceClose));
                return fallback;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Repete um turno ja gerado, restaurando o estado que ele tinha
        // produzido (historico e carta indicada), para que um ramo novo aberto
        // depois dele seja gerado com o mesmo contexto da primeira visita.
        private DynamicTurnResult ReplayCachedTurn(CachedTurn cached)
        {
            LastTurnFromCache = true;
            _history.Add(new ChatMessage("assistant", cached.result.fala));
            _pendingCard = cached.pendingCardAfter;
            SessionLogger.Log("ia_turno_repetido",
                ("npc", SessionLogger.NomeNpc(this)),
                ("turno", TurnNumber),
                ("papel_escolhido", _chosenRole ?? "(abertura)"),
                ("fala", cached.result.fala));
            return cached.result;
        }

        private int TurnNumber => _turnIndex + 1;
        private bool IsLastTurn => TurnNumber >= config.maxTurns;

        private bool ChoseRevealRequest =>
            _chosenRole == OptionRoles.PedidoCarta || _chosenRole == OptionRoles.Insistir;

        // Impõe as regras estruturais do esqueleto sobre a resposta do modelo.
        // Toda correcao feita aqui vai para o log (ia_ajuste_estrutura), para
        // o TCC poder reportar quantas vezes o modelo precisou ser corrigido.
        private void EnforceSkeleton(DynamicTurnResult result, List<string> cardNames)
        {
            var ajustes = new List<string>();
            string reveal = result.revelar_pista ?? "";
            bool ownsReveal = cardNames.Contains(reveal);

            if (ChoseRevealRequest)
            {
                // O jogador pediu a carta: ela e entregue neste turno, sempre.
                string target = ownsReveal ? reveal
                              : cardNames.Contains(_pendingCard ?? "") ? _pendingCard
                              : cardNames.Count > 0 ? cardNames[Random.Range(0, cardNames.Count)] : "";
                if (target != reveal)
                    ajustes.Add($"revelar_pista '{reveal}' -> '{target}' (o jogador pediu a carta)");
                result.revelar_pista = target;
            }
            else if (!string.IsNullOrEmpty(reveal))
            {
                // Os roteirizados so entregam carta quando o jogador pede.
                ajustes.Add($"revelar_pista '{reveal}' removida (entrega sem pedido do jogador)");
                result.revelar_pista = "";
            }

            bool mustEnd = !string.IsNullOrEmpty(result.revelar_pista)
                           || _chosenRole == OptionRoles.Saida
                           || _chosenRole == OptionRoles.Acusar
                           || IsLastTurn;
            if (mustEnd && !result.encerrar)
            {
                ajustes.Add("encerrar false -> true");
                result.encerrar = true;
            }
            else if (!mustEnd && result.encerrar)
            {
                // Os roteirizados so terminam por entrega, saida, acusacao ou
                // limite de turnos - nunca "porque o assunto acabou".
                ajustes.Add("encerrar true -> false (nenhuma condicao de fim do roteiro)");
                result.encerrar = false;
            }

            // No esqueleto, a opcao de pedir a carta so aparece a partir do
            // turno 3 (a entrega mais cedo e no turno 4). Antes disso, vira
            // um aprofundamento comum.
            if (result.papeis_opcoes != null && TurnNumber <= 2)
            {
                for (int i = 0; i < result.papeis_opcoes.Length; i++)
                {
                    string role = result.papeis_opcoes[i];
                    if (role == OptionRoles.PedidoCarta || role == OptionRoles.Insistir)
                    {
                        ajustes.Add($"papel '{role}' no turno {TurnNumber} -> '{OptionRoles.Aprofundar}'");
                        result.papeis_opcoes[i] = OptionRoles.Aprofundar;
                    }
                }
            }

            if (!result.encerrar && result.papeis_opcoes != null)
            {
                int pedido = System.Array.IndexOf(result.papeis_opcoes, OptionRoles.PedidoCarta);
                int insistir = System.Array.IndexOf(result.papeis_opcoes, OptionRoles.Insistir);
                if (pedido >= 0 || insistir >= 0)
                {
                    string alvo = result.carta_alvo ?? "";
                    if (cardNames.Contains(alvo))
                        _pendingCard = alvo;
                    else if (pedido >= 0 && !cardNames.Contains(_pendingCard ?? "") && cardNames.Count > 0)
                    {
                        _pendingCard = cardNames[Random.Range(0, cardNames.Count)];
                        ajustes.Add($"carta_alvo vazia/invalida -> '{_pendingCard}'");
                    }
                }
            }

            if (ajustes.Count > 0)
            {
                SessionLogger.Log("ia_ajuste_estrutura",
                    ("npc", SessionLogger.NomeNpc(this)),
                    ("turno", TurnNumber),
                    ("papel_escolhido", _chosenRole ?? "(abertura)"),
                    ("ajustes", ajustes.ToArray()));
            }
        }

        // Embaralha as opcoes (junto com os papeis): sem isto o modelo tende a
        // por o pedido da carta sempre na mesma posicao, e os .yarn tambem sao
        // embaralhados a mao.
        private static void ShuffleOptions(DynamicTurnResult result)
        {
            if (result.opcoes == null || result.papeis_opcoes == null ||
                result.opcoes.Length != result.papeis_opcoes.Length)
                return;

            for (int i = result.opcoes.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (result.opcoes[i], result.opcoes[j]) = (result.opcoes[j], result.opcoes[i]);
                (result.papeis_opcoes[i], result.papeis_opcoes[j]) = (result.papeis_opcoes[j], result.papeis_opcoes[i]);
            }
        }

        // Log de sessao (RNF09): prompt completo, historico e as cartas que o
        // modelo recebeu neste turno. Ver SessionLogger.
        private void LogRequest(string systemPrompt)
        {
            var historico = new List<string>();
            foreach (ChatMessage m in _history)
                historico.Add($"{m.role}: {m.content}");

            var inventory = GetComponent<LocalInventory>();
            SessionLogger.Log("ia_requisicao",
                ("npc", SessionLogger.NomeNpc(this)),
                ("turno", TurnNumber),
                ("papel_escolhido", _chosenRole ?? "(abertura)"),
                ("carta_pendente", _pendingCard ?? ""),
                ("cartas_do_npc", SessionLogger.Cartas(inventory != null ? inventory.GetAllClues() : null)),
                ("historico", historico.ToArray()),
                ("prompt_sistema", systemPrompt));
        }

        // Resposta como o modelo a devolveu, antes dos ajustes de estrutura.
        private void LogResponse(DynamicTurnResult result)
        {
            SessionLogger.Log("ia_resposta",
                ("npc", SessionLogger.NomeNpc(this)),
                ("turno", TurnNumber),
                ("latencia_ms", _lastLatencyMs),
                ("fala", result.fala),
                ("opcoes", result.opcoes),
                ("papeis_opcoes", result.papeis_opcoes),
                ("encerrar", result.encerrar),
                ("revelar_pista", result.revelar_pista ?? ""),
                ("carta_alvo", result.carta_alvo ?? ""),
                ("resposta_bruta", _client.LastRawContent));
        }

        // Cartas REAIS sorteadas para este NPC na partida (lidas do
        // LocalInventory no mesmo GameObject), mais as escritas a mao no
        // Inspector (usadas em teste).
        //
        // Isto substitui o antigo InventoryNotifier, que "avisava" as cartas ao
        // NPC por uma mensagem de chat da Convai - necessario porque a Convai
        // guardava o estado da conversa no servidor. Aqui as cartas entram no
        // prompt a cada requisicao, entao nao ha nada a avisar de antemao.
        private List<Clue> CollectInventoryClues()
        {
            var clues = new List<Clue>();
            var inventory = GetComponent<LocalInventory>();

            // Depois da primeira entrega, o personagem so tem ESSA carta para
            // mostrar em conversa (regra de uma carta por partida, ver
            // LocalInventory.RevealedInConversation). Restringir aqui faz o
            // prompt e o enum do schema so conhecerem ela: as dicas de um ramo
            // novo apontam para a carta que de fato sera mostrada.
            if (inventory != null && inventory.RevealedInConversation != null)
            {
                clues.Add(inventory.RevealedInConversation);
                return clues;
            }

            if (inventory != null)
            {
                foreach (Clue clue in inventory.GetAllClues())
                {
                    if (clue != null && !string.IsNullOrEmpty(clue.evidenceName))
                        clues.Add(clue);
                }
            }
            return clues;
        }

        private List<string> CollectClueCardNames()
        {
            var names = new List<string>();
            foreach (Clue clue in CollectInventoryClues())
                names.Add(clue.evidenceName);
            foreach (string extra in clueCardNames)
                if (!string.IsNullOrEmpty(extra) && !names.Contains(extra))
                    names.Add(extra);
            return names;
        }

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();

            // Enquadramento do papel. Sem isto o modelo assume o papel de
            // NARRADOR do jogo: descreve a cena em terceira pessoa e propoe
            // ACOES do jogador ("Observar a Senhorita Vermelho") em vez de
            // FALAS do detetive. Isso nao pode depender de o autor lembrar de
            // escrever essas regras em cada persona.
            // (O prompt de geracao dos .yarn - diario, secao 08 - copia este
            // bloco e as regras de estilo abaixo. Mudou aqui, mude la.)
            sb.AppendLine("Voce interpreta UM personagem sendo interrogado por um detetive em um " +
                           "jogo de misterio. Regras invioláveis do seu papel:");
            sb.AppendLine("- Fale SEMPRE em primeira pessoa, como o personagem. Voce nao e narrador.");
            sb.AppendLine("- Nunca descreva a cena, o ambiente, nem as acoes de outras pessoas em " +
                           "terceira pessoa. Voce so diz o que o SEU personagem fala em voz alta.");
            sb.AppendLine("- As opcoes que voce devolve sao FALAS DO DETETIVE dirigidas a voce " +
                           "(perguntas ou afirmacoes que ele diz), nunca acoes do jogador e nunca " +
                           "falas suas.");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(config.sharedSceneContext))
            {
                sb.AppendLine(config.sharedSceneContext);
                sb.AppendLine();
            }

            // Nome do personagem: sem isto o modelo nao sabe quem esta
            // interpretando quando a persona ainda nao foi escrita.
            sb.AppendLine($"Voce e: {SessionLogger.NomeNpc(this)}.");
            sb.AppendLine();

            if (string.IsNullOrWhiteSpace(personaBackstory))
            {
                Debug.LogWarning($"[DynamicNPCController] '{gameObject.name}' está sem " +
                                 "'personaBackstory' - o modelo vai inventar uma personalidade " +
                                 "genérica, diferente da dos NPCs roteirizados.");
            }
            else
            {
                sb.AppendLine("Seu personagem:");
                sb.AppendLine(personaBackstory);
                sb.AppendLine();
            }

            sb.AppendLine("Cartas de pista que voce possui (quando voce entrega uma, o jogo a mostra " +
                           "ao detetive):");
            foreach (Clue clue in CollectInventoryClues())
                sb.AppendLine($"- {clue.evidenceName} ({clue.type})");
            foreach (string extra in clueCardNames)
                if (!string.IsNullOrEmpty(extra))
                    sb.AppendLine($"- {extra}");
            sb.AppendLine();

            sb.AppendLine("Regras de estilo OBRIGATORIAS:");
            sb.AppendLine($"- Cada fala sua tem no maximo {config.maxChars} caracteres e no maximo duas frases.");
            sb.AppendLine($"- Ofereca exatamente {config.optionCount} opcoes. Cada opcao e uma fala curta " +
                           "do detetive, com no maximo 70 caracteres, dita diretamente a voce - nunca uma " +
                           "instrucao ao jogador (nada de \"Insista...\", \"Pergunte...\").");
            sb.AppendLine("- Nao use aspas nas falas nem nas opcoes.");
            sb.AppendLine("- Nunca saia do personagem, nunca mencione que voce e uma IA.");
            sb.AppendLine("- Nao repita frases, expressoes marcantes nem a mesma estrutura de frase de " +
                           "turnos anteriores. Nao copie frases da descricao do seu personagem: diga com " +
                           "outras palavras.");
            sb.AppendLine("- Nunca diga o nome de uma carta nem a descreva de um jeito que a identifique " +
                           "(profissao, cargo, parentesco, detalhes do objeto). Voce so pode aludir ao tipo: " +
                           "uma arma, um comodo, uma pessoa.");
            sb.AppendLine("- Ao entregar uma carta, nao insinue que ela esta ligada ao crime (nada de manchas, " +
                           "sangue ou \"pode ser a arma do crime\").");
            sb.AppendLine("- Nunca use as palavras carta, pista ou prova como se fossem objetos do jogo " +
                           "(nada de \"mostro esta carta\"): voce simplesmente mostra ou entrega algo ao detetive.");
            sb.AppendLine("- So fale do seu alibi quando o detetive perguntar onde voce estava ou o que " +
                           "fazia. Nunca sugira, nas opcoes, a brecha do seu alibi.");
            sb.AppendLine("- Nunca afirme quem e o culpado, qual foi a arma ou o comodo do crime.");
            sb.AppendLine();

            sb.AppendLine("Campos da resposta:");
            sb.AppendLine("- papeis_opcoes: o papel de cada opcao, na mesma ordem das opcoes: tema, " +
                           "aprofundar, pedido_carta, pressionar, insistir, acusar ou saida.");
            sb.AppendLine("- carta_alvo: o nome exato da carta a que a opcao pedido_carta se refere; " +
                           "\"\" se nao houver opcao pedido_carta.");
            sb.AppendLine("- revelar_pista: o nome exato da carta que voce entrega neste turno, ou \"\".");
            sb.AppendLine("- encerrar: true se a conversa termina neste turno.");
            sb.AppendLine();

            sb.AppendLine($"ROTEIRO DESTE TURNO (turno {TurnNumber} de no maximo {config.maxTurns}):");
            sb.AppendLine(BuildTurnDirective());

            return sb.ToString();
        }

        // Sorteia (uma vez por conversa) a carta que o NPC vai indicar. E o
        // codigo que escolhe, e nao o modelo: sozinho, ele indica quase sempre
        // um comodo ("notei algo num comodo"). O sorteio varia o tipo, como
        // os ramos do esqueleto dos .yarn (RevelaArma/RevelaLocal/RevelaSuspeito).
        private void EnsurePendingCard()
        {
            List<Clue> clues = CollectInventoryClues();
            if (!string.IsNullOrEmpty(_pendingCard) && CollectClueCardNames().Contains(_pendingCard))
                return;
            if (clues.Count > 0)
                _pendingCard = clues[Random.Range(0, clues.Count)].evidenceName;
            else if (clueCardNames.Count > 0)
                _pendingCard = clueCardNames[Random.Range(0, clueCardNames.Count)];
        }

        private string DescribePendingCardType()
        {
            Clue clue = CollectInventoryClues().Find(c => c.evidenceName == _pendingCard);
            if (clue == null) return "uma pista";
            switch (clue.type)
            {
                case "arma do crime": return "uma arma";
                case "local": return "um comodo";
                case "suspeito": return "uma pessoa";
                default: return "uma pista";
            }
        }

        // O roteiro de cada turno espelha o esqueleto dos .yarn (diario,
        // secao 08), de acordo com o papel da opcao escolhida pelo jogador.
        private string BuildTurnDirective()
        {
            bool revealOrIndicio = ChoseRevealRequest || _chosenRole == OptionRoles.Pressionar ||
                                   (!IsLastTurn && TurnNumber >= 3 && _chosenRole != OptionRoles.Saida &&
                                    _chosenRole != OptionRoles.Acusar);
            if (revealOrIndicio)
                EnsurePendingCard();

            string card = string.IsNullOrEmpty(_pendingCard) ? "uma das suas cartas" : $"a carta {_pendingCard}";

            if (_chosenRole == OptionRoles.Saida)
                return "O detetive decidiu encerrar a conversa. Despeca-se em uma frase curta, no seu tom. " +
                       "encerrar = true, revelar_pista = \"\".";

            if (_chosenRole == OptionRoles.Acusar)
                return "O detetive te acusou diretamente. Recuse-se a continuar a conversa, no seu tom. " +
                       "encerrar = true, revelar_pista = \"\".";

            if (ChoseRevealRequest)
                return $"O detetive pediu (ou insistiu) sobre o que voce indicou. Voce cede e entrega {card} " +
                       "agora. Sua fala acompanha a entrega (voce esta mostrando algo a ele), sem dizer o nome " +
                       "da carta. revelar_pista = o nome exato dessa carta, encerrar = true.";

            if (IsLastTurn)
                return "Este e o ultimo turno. Encerre a conversa com uma despedida no seu tom, sem entregar " +
                       "carta. encerrar = true, revelar_pista = \"\".";

            if (TurnNumber == 1)
                return "Abertura: receba o detetive no seu tom, sem entregar nada e sem contar o seu alibi. " +
                       "Opcoes, todas com papel tema: (1) sua relacao com o Sr. Vargas, (2) o que voce fez " +
                       "durante a festa, (3) o que voce acha dos outros convidados. encerrar = false.";

            if (_chosenRole == OptionRoles.Pressionar)
                return $"O detetive insistiu que voce esconde algo. Fique na defensiva, mas de sinais de que " +
                       $"pode ceder sobre {card}, sem nomea-la. Opcoes: uma fala do detetive exigindo, pela " +
                       "ultima vez, que voce mostre o que sabe (papel insistir); uma fala em que ele te acusa " +
                       "diretamente (papel acusar); e uma em que ele recua e encerra a conversa (papel saida). " +
                       $"carta_alvo = {(_pendingCard ?? "a carta indicada")}. encerrar = false.";

            if (TurnNumber == 2)
                return "Responda ao que o detetive perguntou, sem entregar pistas. Opcoes: duas que aprofundam " +
                       "o assunto (papel aprofundar) e uma em que o detetive agradece e encerra a conversa " +
                       "(papel saida). carta_alvo = \"\". encerrar = false.";

            if (string.IsNullOrEmpty(_pendingCard))
                return "Responda sem entregar pistas. Opcoes: duas que aprofundam o assunto (papel aprofundar) " +
                       "e uma em que o detetive encerra a conversa (papel saida). carta_alvo = \"\". encerrar = false.";

            return $"Responda e deixe escapar, sem nomear, um indicio ligado a sua carta {_pendingCard} " +
                   $"({DescribePendingCardType()}): algo que voce notou ou sabe sobre ela, dito do seu jeito. " +
                   $"carta_alvo = {_pendingCard}. Opcoes: uma em que o detetive pede diretamente esse indicio " +
                   "(papel pedido_carta), uma em que ele insiste que voce esconde algo (papel pressionar) e uma " +
                   "em que ele encerra a conversa (papel saida). encerrar = false.";
        }
    }
}
