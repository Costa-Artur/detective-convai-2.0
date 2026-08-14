using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System;
using Convai.Scripts.Runtime.Core;

public class PlayerCheckClues : MonoBehaviour
{
    [Header("Panel para exibir pistas do inventário")]
    public GameObject playerCluesPanel;
    [Header("Textos para exibir pistas")]
    public TMP_Text personEvidenceName;
    public TMP_Text weaponEvidenceName;
    public TMP_Text roomEvidenceName;
    [Header("Inventário do jogador")]
    public LocalInventory playerInventory;
    private InterrogationController interrogationController;

    private void Awake() {
        interrogationController = GetComponent<InterrogationController>();
    }

    public void OpenPlayerCluesPanel()
    {
        int currentIndex = interrogationController.GetCurrentIndex();
        interrogationController.CloseNPCDialog(currentIndex);

        string personEvidenceTemp = "", weaponEvidenceTemp = "", roomEvidenceTemp = "";
        foreach (Clue clue in playerInventory.GetAllClues())
        {
            switch (clue.type)
            {
                case "suspeito":
                    personEvidenceTemp += clue.evidenceName + "\n"; 
                    break;
                case "arma do crime":
                    weaponEvidenceTemp += clue.evidenceName + "\n"; 
                    break;
                case "local":
                    roomEvidenceTemp += clue.evidenceName + "\n"; 
                    break;
            }
        }

        personEvidenceName.text = personEvidenceTemp;
        weaponEvidenceName.text = weaponEvidenceTemp;
        roomEvidenceName.text = roomEvidenceTemp;
        
        playerCluesPanel.SetActive(true);
    }

    public void ClosePlayerCluesPanel()
    {
        int currentIndex = interrogationController.GetCurrentIndex();
        interrogationController.ResumeNPCDialog(currentIndex);

        // Código existente para fechar o painel de pistas
        playerCluesPanel.SetActive(false);
    }

}
