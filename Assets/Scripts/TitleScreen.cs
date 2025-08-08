using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
public class TitleScreen : MonoBehaviour
{
    public GameObject CreditsPanel;

    // Start is called before the first frame update
    void Start()
    {
        CreditsPanel.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    //Starts the game when the play button is clicked
    public void StartGame()
    {
        SceneManager.LoadScene("HomeScreen");
    }
    //Quits the game when in the editor or in a build
    public void QuitGame()
    {
        //Check to see if you are inside of the unity editor
        //If so, Exit Play mode
        #if UNITY_EDITOR
        UnityEditor. EditorApplication.isPlaying = false;
        #endif
        Application.Quit();
    }

    public void OpenCredits()
    {
        CreditsPanel.SetActive(true);
    }

    public void CloseCredits()
    {
        CreditsPanel.SetActive(false);
    }

}