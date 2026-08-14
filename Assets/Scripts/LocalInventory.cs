using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Yarn.Unity;
using Convai.Scripts.Runtime.Core;

public class LocalInventory : MonoBehaviour
{
    public List<Clue> inventoryOfClues; //{ get; private set; }
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
        Clue revealedClue = null;

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
        }

        if (revealedClue != null)
        {
            revealCardText.text = $"{gameObject.GetComponent<ConvaiNPC>().characterName} revela a pista do tipo '{clueType}': {revealedClue.evidenceName}";
            revealCardPanel.SetActive(true);
            // Aqui você pode enviar a carta para o sistema de diálogo ou atualizar a UI do jogador
        }
        else
        {
            revealCardText.text =$"{gameObject.GetComponent<ConvaiNPC>().characterName} não tem pistas do tipo '{clueType}' para revelar.";
            revealCardPanel.SetActive(true);
        }
    }

}
