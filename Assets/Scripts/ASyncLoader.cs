using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ASyncLoader : MonoBehaviour
{
    [Header("Menu Screen")]
    [SerializeField]
    private GameObject loadingScreen;
    [SerializeField]
    private GameObject mainMenu;

    [Header("Slider")]
    [SerializeField]
    private Slider loadingSlider;

    public void LoadLevelBtn(string levelToLoad)
    {
        mainMenu.SetActive(false);
        loadingScreen.SetActive(true);

        //Executar a operação ASync
        StartCoroutine(LoadLevelASync(levelToLoad));
    }

    IEnumerator LoadLevelASync(string levelToLoad)
    {
        // Calibra a latência real desta sessão (modelo + rede deste jogador) antes
        // de entrar no jogo - alimenta o delay artificial dos NPCs roteirizados.
        // Ver docs/arquitetura-npc-dinamico.md §5.
        var calibrator = Detective.Dialogue.SessionLatencyCalibrator.Instance;
        if (calibrator != null)
        {
            System.Threading.Tasks.Task calibration = calibrator.Calibrate();
            while (!calibration.IsCompleted)
            {
                loadingSlider.value = 0.15f; // progresso simbólico durante a sondagem
                yield return null;
            }
        }

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(levelToLoad);

        while (!loadOperation.isDone)
        {
            float progressValue = Mathf.Clamp01(loadOperation.progress / 0.9f);
            loadingSlider.value = progressValue;
            yield return null;
        }
    }
}
