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
    public DialogueRunner dialogRunner; // Referência ao DialogRunner do Yarn Spinner

    private int currentIndex = 0; // Índice do personagem atual
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
        List<int> convaiNPCs = new List<int>();

        // Seleciona metade dos NPCs para usar Convai
        while (convaiNPCs.Count < npcCount / 2)
        {
            int randomIndex = UnityEngine.Random.Range(0, npcCount);
            if (!convaiNPCs.Contains(randomIndex))
            {
                convaiNPCs.Add(randomIndex);
                dialogStyle[randomIndex] = "Convai";
            }
        }

        // Define Yarn Spinner para os outros NPCs
        for (int i = 0; i < npcCount; i++)
        {
            if (!dialogStyle.ContainsKey(i))
            {
                dialogStyle[i] = "YarnSpinner";
            }
        }
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

    // Configura o diálogo do NPC de acordo com o estilo sorteado
    void ConfigureDialogueSystem(int index)
    {
        if (dialogStyle[index] == "Convai")
        {
            player.GetComponentInChildren<ConvaiNPCManager>().rayLength = 4.5f;
            dialogRunner.Stop(); // Para o diálogo de Yarn se estiver ativo
        }
        else if (dialogStyle[index] == "YarnSpinner")
        {
            player.GetComponentInChildren<ConvaiNPCManager>().rayLength = 0f;
            
            dialogRunner.Stop(); // Para o diálogo de Yarn se estiver ativo
            string nodeName = npcContainer.transform.GetChild(index).GetComponent<ConvaiNPC>().characterName.Replace(" ", "") + "Inicio";
            dialogRunner.StartDialogue(nodeName);
        }
    }

    public string GetDialogStyle(int index)
    {
        return dialogStyle.ContainsKey(index) ? dialogStyle[index] : "YarnSpinner";
    }

    public void CloseNPCDialog(int index = -1)
    {
        if (index == -1)
        {
            index = currentIndex;
        }

        if (dialogStyle[index] == "Convai")
        {
            player.GetComponentInChildren<ConvaiNPCManager>().rayLength = 0f;
        }
        else if (dialogStyle[index] == "YarnSpinner")
        {
            dialogRunner.Stop();
        }
    }

    public void ResumeNPCDialog(int index = -1)
    {
        if (index == -1)
        {
            index = currentIndex;
        }

        if (dialogStyle[index] == "Convai")
        {
            player.GetComponentInChildren<ConvaiNPCManager>().rayLength = 4.5f;
        }
        else if (dialogStyle[index] == "YarnSpinner")
        {
            string nodeName = npcContainer.transform.GetChild(index).GetComponent<ConvaiNPC>().characterName.Replace(" ", "") + "Inicio";
            if (!dialogRunner.IsDialogueRunning && dialogRunner.NodeExists(nodeName))
            {
                dialogRunner.Stop();
                dialogRunner.StartDialogue(nodeName);
            }
            else if (nodeName != dialogRunner.CurrentNodeName && dialogRunner.NodeExists(nodeName))
            {
                dialogRunner.Stop();
                dialogRunner.StartDialogue(nodeName);
            }
        }
}


}
