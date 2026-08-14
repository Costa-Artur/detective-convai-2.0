using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public GameObject mainMenuPanel;
    public List<GameObject> tutorialStoryPanels;
    private int tutorialStoryPanelCounter = 0;

    public void LoadMainMenu()
    {
        tutorialStoryPanels[tutorialStoryPanelCounter].SetActive(false);
        mainMenuPanel.SetActive(true);
    }
    public void LoadTutorialStory()
    {
        mainMenuPanel.SetActive(false);
        tutorialStoryPanels[0].SetActive(true);
    }

    public void NextTutorialStoryPanel()
    {
        tutorialStoryPanels[tutorialStoryPanelCounter].SetActive(false);
        tutorialStoryPanelCounter = (tutorialStoryPanelCounter + 1) % tutorialStoryPanels.Count;
        tutorialStoryPanels[tutorialStoryPanelCounter].SetActive(true);
    }

    public void PreviousTutorialStoryPanel()
    {
        tutorialStoryPanelCounter--;
        if (tutorialStoryPanelCounter < 0)
        {
            tutorialStoryPanelCounter = 0;
            LoadMainMenu();
        }
        else
        {
            tutorialStoryPanels[tutorialStoryPanelCounter + 1].SetActive(false);
            tutorialStoryPanels[tutorialStoryPanelCounter].SetActive(true);
        }
    }

    public void ExitGame()
    {
        Application.Quit();
    }
}
