using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Attributes;
using System;

public class FinalAccusation : MonoBehaviour
{
    private GameController gameController; // Referência ao GameController para acessar o envelope de crime
    [Header("Dropdowns para escolher a acusação")]
    public TMP_Dropdown personDropdown; // Dropdown para escolher a pessoa
    public TMP_Dropdown weaponDropdown; // Dropdown para escolher a arma
    public TMP_Dropdown locationDropdown; // Dropdown para escolher o local
    [Header("Panels para exibir")]
    public GameObject resultPanel; // Painel que exibe o resultado da acusação (vitória/derrota)
    public GameObject accusationPanel; // Painel que contém os Dropdowns de escolha
    [Header("Textos de resultado")]
    public TMP_Text resultText; // Texto que exibe o resultado da acusação (dentro de Result Panel)
    public TMP_Text resultPersonText;
    public TMP_Text resultWeaponText;
    public TMP_Text resultLocationText;
    
    private InterrogationController interrogationController; // Referência ao InterrogationController

    [Header("Lista com a acusação do jogador")] [ReadOnly]
    public List<Clue> finalAccusation = new List<Clue>(); // Armazena as escolhas do jogador

    private void Awake() {
        gameController = GetComponent<GameController>();
        interrogationController = GetComponent<InterrogationController>();
    }

    // Exibe o painel de acusação
    public void OpenAccusationPanel()
    {
        // Oculta o chat de conversa durante o palpite
        int currentIndex = interrogationController.GetCurrentIndex();
        interrogationController.CloseNPCDialog(currentIndex); // Fecha o diálogo do NPC
        accusationPanel.SetActive(true);
    }

    // Função para fechar o painel de acusação
    public void CloseAccusationPanel()
    {
        // Restaura o chat de conversa após o palpite
        int currentIndex = interrogationController.GetCurrentIndex();
        interrogationController.ResumeNPCDialog(currentIndex); // Retoma o diálogo do NPC
        accusationPanel.SetActive(false); // Oculta o painel
    }

    // Função para exibir o painel de resultado
    public void ShowResultPanel(string result, string person, string weapon, string location)
    {
        resultText.text = $"Resultado: {result}"; // Substitui o texto placeholder com o resultado
        resultPersonText.text = person;
        resultWeaponText.text = weapon;
        resultLocationText.text = location;
        resultPanel.SetActive(true); // Ativa o painel
    }

    // Chama ao confirmar a acusação
    public void ConfirmAccusation()
    {
        // Pega as escolhas do jogador
        Clue chosenPerson = gameController.GetClueByName(personDropdown.options[personDropdown.value].text);
        Clue chosenWeapon = gameController.GetClueByName(weaponDropdown.options[weaponDropdown.value].text);
        Clue chosenLocation = gameController.GetClueByName(locationDropdown.options[locationDropdown.value].text);

        // Armazena as escolhas na lista
        finalAccusation.Clear();
        finalAccusation.Add(chosenPerson);
        finalAccusation.Add(chosenWeapon);
        finalAccusation.Add(chosenLocation);

        // Obtém o envelope do crime
        List<Clue> crimeEnvelopeCopy = gameController.crimeEnvelope;
        string guiltyPerson = "", guiltyWeapon = "", guiltyLocation = "";
        foreach (Clue clue in crimeEnvelopeCopy)
        {
            switch (clue.type)
            {
                case "suspeito":
                    guiltyPerson = clue.evidenceName;
                    break;
                case "arma do crime":
                    guiltyWeapon = clue.evidenceName;
                    break;
                case "local":
                    guiltyLocation = clue.evidenceName;
                    break;
            }
        }

        // Verifica se a acusação está correta
        if (gameController.IsAccusationCorrect(finalAccusation))
        {
            ShowResultPanel("Você venceu!", guiltyPerson, guiltyWeapon, guiltyLocation);
        }
        else
        {
            ShowResultPanel("Acusação errada! Você perdeu.", guiltyPerson, guiltyWeapon, guiltyLocation);
        }
        accusationPanel.SetActive(false); // Oculta o painel após a confirmação
    }

    // Função para acusações dos NPCs
    public void NPCMakeFinalAccusation(NPCAI npc, Clue person, Clue weapon, Clue location)
    {
        List<Clue> npcAccusation = new List<Clue> { person, weapon, location };

        // Obtém o envelope do crime
        List<Clue> crimeEnvelopeCopy = gameController.crimeEnvelope;
        string guiltyPerson = "", guiltyWeapon = "", guiltyLocation = "";
        foreach (Clue clue in crimeEnvelopeCopy)
        {
            switch (clue.type)
            {
                case "suspeito":
                    guiltyPerson = clue.evidenceName;
                    break;
                case "arma do crime":
                    guiltyWeapon = clue.evidenceName;
                    break;
                case "local":
                    guiltyLocation = clue.evidenceName;
                    break;
            }
        }

        if (gameController.IsAccusationCorrect(npcAccusation))
        {
            // Se a acusação estiver correta, NPC vence
            ShowResultPanel($"{npc.GetComponent<ConvaiNPC>().characterName} fez a acusação correta e venceu o jogo!", guiltyPerson, guiltyWeapon, guiltyLocation);
            // Aqui exibe a tela de derrota para o jogador
        }
        else
        {
            // Se a acusação estiver errada, NPC perde
            npc.HasLost = true;
            resultText.text = $"{npc.GetComponent<ConvaiNPC>().characterName} fez a acusação errada e perdeu!";
            Debug.LogWarning($"{npc.GetComponent<ConvaiNPC>().characterName} fez a acusação errada e perdeu!");
            //ShowResultPanel();
        }
    }

    // Função para exibir o painel de resultado
    public void ShowResultPanel()
    {
        resultPanel.SetActive(true); // Ativa o painel de resultado
    }
}
