using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AudioClipConfig", menuName = "Audio/ClipConfig")]
public class AudioClipConfig : ScriptableObject
{
    [System.Serializable]
    public class AudioEntry
    {
        public string key;
        public AudioClip clip;
    }

    public List<AudioEntry> audioEntries = new List<AudioEntry>();
}
