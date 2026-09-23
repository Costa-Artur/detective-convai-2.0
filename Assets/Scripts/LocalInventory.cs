using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Yarn.Unity;
using Convai.Scripts.Runtime.Core;

public class LocalInventory : MonoBehaviour
{
    public List<Clue> inventoryOfClues; //{ get; private set; }
    private static PlayerCheckClues _playerClues;

    // Regra de jogo: cada personagem entrega UMA carta por partida em
    // conversa. Revisitar o caminho da entrega mostra a mesma carta de novo
    // (a fala "tome, veja isto" continua fazendo sentido), mas sem informação
    // nova - antes cada visita entregava outra carta, e bastava conversar com
    // todos várias vezes para ver as 16 cartas dos NPCs sem nenhum palpite.
    // Não afeta os palpites: neles o personagem mostra qualquer carta que tiver.
    public Clue RevealedInConversation { get; private set; }
    [Header ("Objetos para revelar cartas")]
    public GameObject revealCardPanel;
    public TextMeshProUGUI revealCardText;

    private void Awake()
    {
        inventoryOfClues = new List<Clue>();
    }    

    public void Add(Clue newClue)
    {
        inventoryOfClues.Add(newClue);
    }

    public void Remove(Clue oldClue)
    {
        inventoryOfClues.Remove(oldClue);
    }
    
    // Função para verificar se o NPC tem a pista específica
    public bool HasClue(Clue clue)
    {
        return inventoryOfClues.Contains(clue);
    }

    public List<Clue> GetAllClues()
    {
        return inventoryOfClues;
    }

    // Define o comando Yarn "reveal" para revelar uma carta específica
    [YarnCommand("reveal")]
    public void RevealCard(string clueType = "")
    {
        RevealCardOfType(clueType, "roteirizado");
    }

    // Mesma revelação, mas devolve a carta mostrada (comandos do Yarn só
    // podem retornar void). "origem" vai para o log de sessão: "roteirizado"
    // (comando <<reveal>>) ou "dinamico" (campo revelar_pista da IA).
    public Clue RevealCardOfType(string clueType, string origem)
    {
        if (RevealedInConversation != null)
        {
            bool outroTipo = !string.IsNullOrEmpty(clueType) && RevealedInConversation.type != clueType;
            ShowRevealedCard(RevealedInConversation, clueType, origem, outroTipo, repetida: true);
            return RevealedInConversation;
        }

        Clue revealedClue = null;
        bool usouCartaAleatoria = false;

        // Filtra as pistas pelo tipo, se especificado
        if (!string.IsNullOrEmpty(clueType))
        {
            var cluesOfType = inventoryOfClues.FindAll(clue => clue.type == clueType);
            if (cluesOfType.Count > 0)
            {
                revealedClue = cluesOfType[Random.Range(0, cluesOfType.Count)];
            }
        }

        // Se nenhuma pista específica for encontrada ou o tipo não for informado, escolhe uma aleatória
        if (revealedClue == null && inventoryOfClues.Count > 0)
        {
            revealedClue = inventoryOfClues[Random.Range(0, inventoryOfClues.Count)];
            usouCartaAleatoria = true;
        }

        RevealedInConversation = revealedClue;
        ShowRevealedCard(revealedClue, clueType, origem, usouCartaAleatoria);
        return revealedClue;
    }

    // Revela uma carta ESPECÍFICA deste NPC - usado pelo NPC dinâmico, que
    // escolhe a carta pelo nome. Retorna null (sem abrir o painel) se a
    // carta não estiver no inventário.
    public Clue RevealSpecificCard(string evidenceName, string origem)
    {
        if (RevealedInConversation != null)
        {
            ShowRevealedCard(RevealedInConversation, RevealedInConversation.type, origem, false, repetida: true);
            return RevealedInConversation;
        }

        Clue clue = inventoryOfClues.Find(c => c != null &&
            string.Equals(c.evidenceName, evidenceName, System.StringComparison.OrdinalIgnoreCase));
        if (clue == null)
            return null;

        RevealedInConversation = clue;
        ShowRevealedCard(clue, clue.type, origem, false);
        return clue;
    }

    // Painel "Pista Revelada" - o mesmo texto para as duas origens. O tipo
    // exibido é sempre o da carta mostrada: antes, quando o .yarn pedia um
    // tipo que o NPC não tinha, o painel dizia "pista do tipo 'local': Faca".
    private void ShowRevealedCard(Clue revealedClue, string requestedType, string origem, bool usouCartaAleatoria,
                                  bool repetida = false)
    {
        Detective.Dialogue.SessionLogger.Log("carta_revelada",
            ("npc", Detective.Dialogue.SessionLogger.NomeNpc(this)),
            ("origem", origem),
            ("tipo_pedido", requestedType ?? ""),
            ("carta_mostrada", Detective.Dialogue.SessionLogger.Carta(revealedClue)),
            ("tipo_diferente_do_pedido", usouCartaAleatoria),
            ("repetida", repetida),
            ("cartas_do_npc", Detective.Dialogue.SessionLogger.Cartas(inventoryOfClues)));

        if (revealedClue != null)
        {
            string characterName = gameObject.GetComponent<ConvaiNPC>().characterName;
            // Na repetição, o texto deixa claro que não há carta nova - igual
            // para as duas origens.
            revealCardText.text = repetida
                ? $"{characterName} mostra de novo a mesma pista: {revealedClue.evidenceName}"
                : $"{characterName} revela a pista do tipo '{revealedClue.type}': {revealedClue.evidenceName}";
            revealCardPanel.SetActive(true);

            // Anota a carta para o painel "Verificar Pistas". Antes ela só
            // aparecia neste painel e se perdia ao fechá-lo.
            if (_playerClues == null)
                _playerClues = FindFirstObjectByType<PlayerCheckClues>();
            if (_playerClues != null)
                _playerClues.RegisterRevealedClue(revealedClue, characterName);
        }
        else
        {
            revealCardText.text =$"{gameObject.GetComponent<ConvaiNPC>().characterName} não tem pistas do tipo '{requestedType}' para revelar.";
            revealCardPanel.SetActive(true);
        }
    }

}
