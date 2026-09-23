using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using Convai.Scripts.Runtime.Core;
using TMPro; // Certifique-se de incluir esta linha para o TextMeshPro
using Yarn.Unity;
using System;

public class InterrogationController : MonoBehaviour
{
    public List<CinemachineVirtualCamera> characterCameras; // Lista de todas as VCams
    public List<Transform> characterPositions; // Posições dos personagens (NPCs)
    public Transform player; // O jogador a ser movido
    public float distanceOffset = 2.0f; // Distância segura para evitar colisão
    public GameObject npcContainer; // Referência ao GameObject que contém os NPCs
    public TextMeshProUGUI characterNameText; // Referência ao TextMeshPro para o nome do NPC
    [Tooltip("Não é mais usado diretamente aqui - cada YarnDialogueSource tem a própria " +
             "referência ao DialogueRunner. Mantido para não quebrar a referência da cena.")]
    public DialogueRunner dialogRunner;

    [Tooltip("Interface única de diálogo usada pelos 6 NPCs - roteirizados e dinâmico. " +
             "Ver docs/arquitetura-npc-dinamico.md §3.")]
    public Detective.Dialogue.UnifiedDialogueUI unifiedDialogueUI;

    private int currentIndex = 0; // Índice do personagem atual
    private int conversationIndex = -1; // Personagem cuja conversa está aberta (-1 = nenhuma)
    private Dictionary<int, string> dialogStyle = new Dictionary<int, string>(); // Dicionário para estilo de diálogo (Convai ou Yarn Spinner)
 

    void Start()
    {
        InitializeDialogStyles(); // Define o estilo de diálogo para cada NPC
        // Ativa apenas a câmera do primeiro personagem inicialmente
        SetActiveCamera(currentIndex);
    }

    // Define aleatoriamente os estilos de diálogo para os NPCs
    void InitializeDialogStyles()
    {
        int npcCount = npcContainer.transform.childCount;

        int randomIndex = UnityEngine.Random.Range(0, npcCount);
        dialogStyle[randomIndex] = "Convai";

        // Define Yarn Spinner para os outros NPCs
        for (int i = 0; i < npcCount; i++)
        {
            if (!dialogStyle.ContainsKey(i))
            {
                dialogStyle[i] = "YarnSpinner";
            }
        }

        // ATENÇÃO: este log revela a resposta do experimento. É auxílio de
        // DESENVOLVIMENTO - precisa ser removido (ou trocado por gravação em
        // arquivo de log da sessão) antes dos testes com participantes, senão
        // qualquer um que abra o Console descobre qual NPC é o de IA.
        Transform chosen = npcContainer.transform.GetChild(randomIndex);
        Debug.Log($"<color=cyan>[DEV] NPC dinâmico desta sessão: índice {randomIndex} — " +
                  $"'{chosen.name}'</color>");

        // Registro permanente do sorteio (a resposta certa do RF13), junto da
        // configuração do modelo usada nesta partida.
        var dynamicController = chosen.GetComponent<Detective.Dialogue.DynamicNPCController>();
        Detective.Dialogue.AzureOpenAIConfig cfg = dynamicController != null ? dynamicController.config : null;
        Detective.Dialogue.SessionLogger.Log("npc_dinamico_sorteado",
            ("indice", randomIndex),
            ("npc", Detective.Dialogue.SessionLogger.NomeNpc(chosen)),
            ("objeto", chosen.name),
            ("modelo", cfg != null ? cfg.deploymentName : "(sem config)"),
            ("reasoning_effort", cfg != null && cfg.isReasoningModel ? cfg.reasoningEffort : "(n/a)"),
            ("option_count", cfg != null ? cfg.optionCount : 0),
            ("max_turns", cfg != null ? cfg.maxTurns : 0),
            ("max_chars", cfg != null ? cfg.maxChars : 0));
    }

    // Função para navegar pelos personagens e sincronizar o inventário de NPCs
    public int GetCurrentIndex() => currentIndex;

    [ContextMenu("Next Character")]
    public void NextCharacter()
    {
        currentIndex = (currentIndex + 1) % characterCameras.Count;
        SetActiveCamera(currentIndex);
    }

    [ContextMenu("Previous Character")]
    public void PreviousCharacter()
    {
        currentIndex--;
        if (currentIndex < 0)
        {
            currentIndex = characterCameras.Count - 1;
        }
        SetActiveCamera(currentIndex);
    }

    public void SetNPCByIndex(int index, bool background = false)
    {
        if (index >= 0 && index < characterCameras.Count)
        {
            currentIndex = index; // Atualiza o índice do NPC
            SetActiveCamera(currentIndex, background); // Sincroniza a câmera e a posição do jogador com o NPC atual
        }
    }

    void SetActiveCamera(int index, bool background = false)
    {
        // Desativa todas as câmeras
        foreach (var cam in characterCameras)
        {
            cam.gameObject.SetActive(false);
        }

        // Ativa a câmera do personagem selecionado
        characterCameras[index].gameObject.SetActive(true);

        // Move o jogador para uma posição ajustada próxima ao NPC, sem colidir com ele
        Vector3 directionToNPC = (characterPositions[index].position - player.position).normalized; // Direção do jogador até o NPC
        Vector3 adjustedPosition = characterPositions[index].position - directionToNPC * distanceOffset; // Aplica o offset

        player.position = adjustedPosition; // Move o jogador para a posição ajustada

        // Atualiza o nome do NPC na interface
        
        if(!background) //Liga sistema de diálogos se não for de fundo
        {
            UpdateCharacterName(index);
            ConfigureDialogueSystem(index);
        }
        else
        {
            UpdateCharacterName(index, "Respondendo");
        }
    }

    // Função para atualizar o nome do personagem no TextMeshPro
    void UpdateCharacterName(int index, String currentAction = "Interrogando")
    {
        Transform npcTransform = npcContainer.transform.GetChild(index);
        ConvaiNPC npc = npcTransform.GetComponent<ConvaiNPC>();

        if (npc != null && characterNameText != null)
        {
            characterNameText.text = $"{currentAction}: {npc.characterName}"; // Aqui você pode personalizar a mensagem
        }
    }

    // Abre a conversa com o NPC na interface unificada. Os dois sistemas de
    // diálogo (roteirizado e dinâmico) passam pela MESMA interface - só muda
    // quem gera as falas por trás. Ver docs/arquitetura-npc-dinamico.md §3.
    void ConfigureDialogueSystem(int index)
    {
        Detective.Dialogue.IDialogueSource source = GetDialogueSource(index);
        if (source == null) return;

        conversationIndex = index;
        unifiedDialogueUI.OpenConversation(source);
    }

    // O palpite do jogador e os turnos dos NPCs movem a câmera para quem está
    // respondendo (SetNPCByIndex em segundo plano), mas a conversa aberta
    // continua sendo a do personagem interrogado. Sem isto, a conversa com um
    // personagem voltava na frente da câmera de outro, e "Próximo"/"Anterior"
    // partiam do índice errado.
    public void RestoreConversationView()
    {
        if (conversationIndex < 0)
            return;

        currentIndex = conversationIndex;
        SetActiveCamera(currentIndex, true);
        UpdateCharacterName(currentIndex);
    }

    // Cada NPC carrega os DOIS componentes de origem (roteirizado e dinâmico);
    // o sorteio de InitializeDialogStyles decide qual é usado nesta partida.
    // Isso mantém os 6 personagens configurados de forma idêntica na cena.
    Detective.Dialogue.IDialogueSource GetDialogueSource(int index)
    {
        if (unifiedDialogueUI == null)
        {
            Debug.LogError("[InterrogationController] 'unifiedDialogueUI' não atribuído no Inspector.");
            return null;
        }

        Transform npc = npcContainer.transform.GetChild(index);

        Detective.Dialogue.IDialogueSource source = dialogStyle[index] == "Convai"
            ? npc.GetComponent<Detective.Dialogue.DynamicDialogueSource>()
            : (Detective.Dialogue.IDialogueSource)npc.GetComponent<Detective.Dialogue.YarnDialogueSource>();

        if (source == null)
        {
            Debug.LogError($"[InterrogationController] O NPC '{npc.name}' não tem o componente de " +
                           $"diálogo necessário para o estilo '{dialogStyle[index]}'.");
        }

        return source;
    }

    public string GetDialogStyle(int index)
    {
        return dialogStyle.ContainsKey(index) ? dialogStyle[index] : "YarnSpinner";
    }

    // Esconde o painel de diálogo temporariamente (quando o jogador abre o
    // painel de palpite, de cartas, etc). A conversa NÃO é encerrada - o
    // histórico do NPC dinâmico e a posição na árvore do Yarn são preservados.
    public void CloseNPCDialog(int index = -1)
    {
        if (unifiedDialogueUI != null)
            unifiedDialogueUI.Hide();
    }

    // Volta a exibir a conversa que estava em andamento.
    public void ResumeNPCDialog(int index = -1)
    {
        if (unifiedDialogueUI != null)
            unifiedDialogueUI.Show();
    }
}
