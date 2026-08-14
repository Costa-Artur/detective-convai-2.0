using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Convai.Scripts.Runtime.Core;

public class InventoryNotifier : MonoBehaviour
{
    // Referência ao GameObject que contém todos os NPCs
    public GameObject npcContainer;
    private InterrogationController interrogationController; // Referência ao InterrogationController para controlar o NPC ativo
    public GameObject censorDialogCanvas; // Referência ao Canvas para censurar o Chatbox
    private int currentNPCIndex = 0; // Índice do NPC atual

    [Header("Configurações de Notificação")]
    public bool mutePlayerDuringNotification = true; // Mutar jogador durante notificação
    public bool hideChatMessages = true; // Ocultar mensagens de chat
    public bool hideChatBox = true; // Ocultar ChatBox

    private AudioListener playerAudioListener; // Referência ao AudioListener do jogador
    private GameObject chatContent; // Referência ao Content dentro do Chat Scroll View

    private void Awake() {
        interrogationController = GetComponent<InterrogationController>();
    }
    private void Start()
    {
        // Obtém o AudioListener do jogador
        playerAudioListener = Camera.main.GetComponent<AudioListener>();

        // Localiza o Chat Content no Canvas do chat
        GameObject chatCanvas = GameObject.Find("Convai Transcript Canvas - Mobile Chat (Custom)(Clone)");
        if (chatCanvas != null)
        {
            chatContent = chatCanvas.transform.Find("Panel/Chat Scroll View/Viewport/Content").gameObject;
        }
    }

    // Função para notificar todos os NPCs sobre os itens do seu inventário
    [ContextMenu("Notify NPCs of Inventory")]
    public async void NotifyNPCsOfInventory()
    {
        while (playerAudioListener == null)
        {
            await Task.Delay(100);
        }
        
        // Mutar o jogador se a opção estiver habilitada
        if (mutePlayerDuringNotification && playerAudioListener != null)
        {
            AudioListener.volume = 0;
            playerAudioListener.enabled = false;
        }
        
        // Censurar o ChatBox se a opção estiver habilitada
        if (hideChatBox && censorDialogCanvas != null)
        {
            censorDialogCanvas.SetActive(true);
        }

        // Percorre todos os NPCs filhos do npcContainer
        currentNPCIndex = 0; // Inicia o índice no primeiro NPC
        foreach (Transform npcTransform in npcContainer.transform)
        {
            // Verifica o estilo de diálogo do NPC pelo InterrogationController
            string dialogStyle = interrogationController.GetDialogStyle(currentNPCIndex);
            
            if (dialogStyle == "Convai") 
            {
                // Obtém o script ConvaiNPC e LocalInventory de cada NPC
                ConvaiNPC npc = npcTransform.GetComponent<ConvaiNPC>();
                LocalInventory inventory = npcTransform.GetComponent<LocalInventory>();

                // Verifica se os componentes foram encontrados
                if (npc != null && inventory != null)
                {
                    // Define o NPC atual usando o índice atual e notifica
                    if (!npc.isCharacterActive)
                    {
                        interrogationController.SetNPCByIndex(currentNPCIndex);
                        await Task.Delay(5000); // Aguardar para garantir troca do NPC ativo
                    } 

                    await ActivateAndNotifyNPC(npc, inventory);
                }
            }
            // Incrementa o índice para o próximo NPC na próxima iteração
            currentNPCIndex++;
        }

        // Reativar o áudio após a notificação, se a opção estiver habilitada
        if (mutePlayerDuringNotification && playerAudioListener != null)
        {
            playerAudioListener.enabled = true;
            AudioListener.volume = 1;
        }

        // Limpar mensagens no chatbox se a opção estiver habilitada
        if (hideChatMessages && chatContent != null)
        {
            ClearChatMessages();
            interrogationController.SetNPCByIndex(0); // Volta ao primeiro NPC
        }

        // Desbloquear o ChatBox se a opção estiver habilitada
        if (hideChatBox && censorDialogCanvas != null)
        {
            censorDialogCanvas.SetActive(false);
        }
    }
    
    // Função para ativar o NPC e enviar a mensagem
    private async Task ActivateAndNotifyNPC(ConvaiNPC npc, LocalInventory inventory)
    {        
        await Task.Delay(100); // Pausa breve para garantir que o NPC ativo foi selecionado

        // Constrói a mensagem com o conteúdo do inventário
        string message = BuildInventoryMessage(inventory.inventoryOfClues);

        // Envia a mensagem para o NPC e espera que ele responda
        await Task.Run(() => npc.SendTextDataAsync(message));
                
        // Aguarda até que o NPC comece a falar (isCharacterTalking seja true)
        while (!npc.IsCharacterTalking)
        {
            await Task.Delay(50); // Verificação a cada 50ms para reduzir o uso de recursos
        }

        // Aguarda até que o NPC termine de falar (isCharacterTalking volte a ser false)
        while (npc.IsCharacterTalking)
        {
            await Task.Delay(4000); // Verificação a cada 2000ms para reduzir o uso de recursos e lidar com pausas nas falas dos NPCs
        }
    }

    // Função auxiliar para construir a mensagem de pistas a partir do inventário
    private string BuildInventoryMessage(List<Clue> clues)
    {
        if (clues.Count == 0)
        {
            return "Você não tem pistas no momento.";
        }

        string message = "As pistas que você possui são:\n";
        foreach (Clue clue in clues)
        {
            message += $"- {clue.evidenceName}\n";
        }

        return message;
    }

    // Função para ocultar mensagens do chat
    private void ClearChatMessages()
    {
        foreach (Transform child in chatContent.transform)
        {
            if (child.name.Contains("MessageBoxCharacter(Clone)"))
            {
                child.gameObject.SetActive(false);
            }
        }
    }
}
