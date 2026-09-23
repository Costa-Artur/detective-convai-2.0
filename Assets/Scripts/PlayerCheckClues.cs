using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System;
using Convai.Scripts.Runtime.Core;
using UnityEngine.UI;

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
    [Header("Bloco de notas (opcional)")]
    [Tooltip("Raiz do bloco de notas com uma caixa (Toggle) por carta. Se vazio, procura o " +
             "objeto 'Clue Sheet' na cena. As caixas são ligadas às cartas pelo texto visível.")]
    public GameObject clueSheet;
    private Dictionary<string, Toggle> sheetToggles;
    private InterrogationController interrogationController;

    // Cartas que o jogador VIU durante a partida: entregues em conversa
    // (<<reveal>> do Yarn ou revelar_pista da IA) ou mostradas em resposta a
    // um palpite. Antes o painel listava só as cartas iniciais e nada era
    // anotado - o TCC original prevê o registro da pista revelada no diário
    // (Figura 24) e o uso do painel para eliminar possibilidades (Apêndice C).
    private readonly List<(Clue clue, string mostradaPor)> revealedClues = new List<(Clue, string)>();

    public void RegisterRevealedClue(Clue clue, string shownBy)
    {
        if (clue == null || clue.id == -1) // -1 = emptyClue do SuggestionSystem
            return;
        if (playerInventory != null && playerInventory.HasClue(clue))
            return;
        if (revealedClues.Exists(r => r.clue == clue))
            return; // cada carta pertence a um único personagem

        revealedClues.Add((clue, shownBy));
        MarkOnSheet(clue);
        Detective.Dialogue.SessionLogger.Log("pista_anotada",
            ("carta", Detective.Dialogue.SessionLogger.Carta(clue)),
            ("mostrada_por", shownBy),
            ("total_vistas", revealedClues.Count));
    }

    private void Awake() {
        interrogationController = GetComponent<InterrogationController>();
    }

    // Chamado pelo GameController logo depois de distribuir as cartas: as do
    // próprio jogador também já estão descartadas no bloco de notas.
    public void MarkPlayerCards()
    {
        foreach (Clue clue in playerInventory.GetAllClues())
            MarkOnSheet(clue);
    }

    // Marca no bloco de notas uma carta que o jogador COM CERTEZA sabe que
    // não está no envelope. A caixa fica travada: distingue a certeza (cinza)
    // das marcações que o jogador faz à mão e evita desmarcar por engano.
    private void MarkOnSheet(Clue clue)
    {
        if (clue == null)
            return;
        if (sheetToggles == null)
            sheetToggles = BuildSheetIndex();

        if (sheetToggles.TryGetValue(Normalize(clue.evidenceName), out Toggle toggle))
        {
            toggle.isOn = true;
            toggle.interactable = false;
        }
        else
        {
            Debug.LogWarning($"[PlayerCheckClues] Bloco de notas sem caixa para a carta '{clue.evidenceName}'.");
        }
    }

    private Dictionary<string, Toggle> BuildSheetIndex()
    {
        var index = new Dictionary<string, Toggle>();
        if (clueSheet == null)
            clueSheet = GameObject.Find("Clue Sheet");
        if (clueSheet == null)
        {
            Debug.LogWarning("[PlayerCheckClues] Bloco de notas ('Clue Sheet') não encontrado - " +
                             "as cartas vistas não serão marcadas nele.");
            return index;
        }

        foreach (Toggle toggle in clueSheet.GetComponentsInChildren<Toggle>(true))
        {
            string label = null;
            var tmp = toggle.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) label = tmp.text;
            var legacy = toggle.GetComponentInChildren<Text>(true);
            if (label == null && legacy != null) label = legacy.text;
            if (!string.IsNullOrEmpty(label))
                index[Normalize(label)] = toggle;
        }
        return index;
    }

    private static string Normalize(string s) => s.Trim().ToLowerInvariant();

    public void OpenPlayerCluesPanel()
    {
        int currentIndex = interrogationController.GetCurrentIndex();
        interrogationController.CloseNPCDialog(currentIndex);

        string personEvidenceTemp = "", weaponEvidenceTemp = "", roomEvidenceTemp = "";

        // Primeiro as cartas do próprio jogador, depois as que ele viu, cada
        // uma com quem a mostrou - as duas descartam possibilidades.
        var linhas = new List<(Clue clue, string origem)>();
        foreach (Clue clue in playerInventory.GetAllClues())
            linhas.Add((clue, "você"));
        foreach (var r in revealedClues)
            linhas.Add((r.clue, r.mostradaPor));

        foreach (var (clue, origem) in linhas)
        {
            string linha = $"{clue.evidenceName} <size=70%>({origem})</size>\n";
            switch (clue.type)
            {
                case "suspeito":
                    personEvidenceTemp += linha;
                    break;
                case "arma do crime":
                    weaponEvidenceTemp += linha;
                    break;
                case "local":
                    roomEvidenceTemp += linha;
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
