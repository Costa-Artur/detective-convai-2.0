using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Attributes;

public class NPCAI : MonoBehaviour
{
    public GameController gameController;
    private SuggestionSystem suggestionSystem;
    private FinalAccusation finalAccusation;
    public bool HasLost = false;
    [ReadOnly]
    public LocalInventory npcInventory; // Inventário do NPC

    [SerializeField]
    private List<Clue> possiblePersons;
    [SerializeField]
    private List<Clue> possibleWeapons;
    [SerializeField]
    private List<Clue> possibleLocations;

    private void Awake() {
        suggestionSystem = gameController.GetComponent<SuggestionSystem>();
        finalAccusation = gameController.GetComponent<FinalAccusation>();
        npcInventory = GetComponent<LocalInventory>();
    }
    void Start()
    {
        // Inicializa as listas de possibilidades
        possiblePersons = gameController.GetAllPersons();
        possibleWeapons = gameController.GetAllWeapons();
        possibleLocations = gameController.GetAllLocations();
    }

    [ContextMenu("Play Turn")]
    public void PlayTurn()
    {
        if (HasLost)
        {
            Debug.Log($"{GetComponent<ConvaiNPC>().characterName} já perdeu e não pode mais jogar.");
            return; // Se o NPC perdeu, ele não joga mais
        }
        // Continua o turno normalmente se não tiver perdido

        // Verifica cartas no inventário e elimina possibilidades
        UpdatePossibleClues();

        // Faz um palpite se não tiver certeza ainda
        if (!HasFinalAccusation())
        {
            MakeSuggestion();
        }
        else
        {
            // Acusação final se tiver certeza
            MakeFinalAccusation();
        }
    }

    void UpdatePossibleClues()
    {
        // Elimina as opções com base no inventário do NPC
        foreach (Clue clue in npcInventory.GetAllClues())
        {
            if (possiblePersons.Contains(clue))
                possiblePersons.Remove(clue);
            if (possibleWeapons.Contains(clue))
                possibleWeapons.Remove(clue);
            if (possibleLocations.Contains(clue))
                possibleLocations.Remove(clue);
        }
    }

    void MakeSuggestion()
    {
        // Escolhe aleatoriamente entre as opções restantes
        Clue guessedPerson = possiblePersons[Random.Range(0, possiblePersons.Count)];
        Clue guessedWeapon = possibleWeapons[Random.Range(0, possibleWeapons.Count)];
        Clue guessedLocation = possibleLocations[Random.Range(0, possibleLocations.Count)];
        
        Debug.Log($"Palpite do NPC: {guessedPerson.evidenceName}, {guessedWeapon.evidenceName}, {guessedLocation.evidenceName}");

        // Simula o palpite passando por outros NPCs e obtendo respostas
        Clue cluesRevealed = suggestionSystem.NPCMakeSuggestion(this, guessedPerson, guessedWeapon, guessedLocation);

        // Atualiza o Clue Sheet do NPC com base na carta revelada
        if (cluesRevealed.id != -1) // Verifica se não é a emptyClue
        {
            if (possiblePersons.Contains(cluesRevealed))
                possiblePersons.Remove(cluesRevealed);
            if (possibleWeapons.Contains(cluesRevealed))
                possibleWeapons.Remove(cluesRevealed);
            if (possibleLocations.Contains(cluesRevealed))
                possibleLocations.Remove(cluesRevealed);
                
            Debug.Log($"Carta revelada foi removida: {cluesRevealed.evidenceName}");
        }
        else
        {
            Debug.LogWarning("Nenhuma carta válida foi revelada. É possível que seja o turno do jogador, que sempre retorna uma emptyClue.");
        }       
    }

    bool HasFinalAccusation()
    {
        // Verifica se sobrou apenas uma opção para cada tipo de pista
        return possiblePersons.Count == 1 && possibleWeapons.Count == 1 && possibleLocations.Count == 1;
    }

    [ContextMenu("Make Final Accusation")]
    void MakeFinalAccusation()
    {
        Clue finalPerson = possiblePersons[0];
        Clue finalWeapon = possibleWeapons[0];
        Clue finalLocation = possibleLocations[0];

        // Avisa ao jogador que o NPC fez uma acusação
        finalAccusation.NPCMakeFinalAccusation(this, finalPerson, finalWeapon, finalLocation);
    }

    public void SeePlayerClue(Clue clueRevealed)
    {
        Debug.Log("NPC viu a carta: "+ clueRevealed.evidenceName);
        // Atualiza o Clue Sheet do NPC com base na carta revelada
        if (clueRevealed.id != -1) // Verifica se não é a emptyClue
        {
            if (possiblePersons.Contains(clueRevealed))
                possiblePersons.Remove(clueRevealed);
            if (possibleWeapons.Contains(clueRevealed))
                possibleWeapons.Remove(clueRevealed);
            if (possibleLocations.Contains(clueRevealed))
                possibleLocations.Remove(clueRevealed);
        }
    }

    public void InitializeClues(List<Clue> allClues)
    {
        possiblePersons = allClues.FindAll(clue => clue.type == "suspeito");
        possibleWeapons = allClues.FindAll(clue => clue.type == "arma do crime");
        possibleLocations = allClues.FindAll(clue => clue.type == "local");
    }

}
