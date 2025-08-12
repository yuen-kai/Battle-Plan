using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Uisoundeffects : MonoBehaviour
{
    //References
    private AudioSource sfxSource;
    private AudioManager audiomanager;
    // Start is called before the first frame update
    void Start()
    {
        sfxSource = GameObject.Find("SFXSource").GetComponent<AudioSource>();
        audiomanager = GameObject.Find("AudioManager").GetComponent<AudioManager>();
    }

    // Update is called once per frame
    void Update()
    {
      
    }
    public void PlayClickSFX()
    {
        sfxSource.PlayOneShot(audiomanager.buttonClick);
    }
}
