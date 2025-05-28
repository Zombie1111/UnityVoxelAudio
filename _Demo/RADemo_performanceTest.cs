using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RaytracedAudio;

namespace RaytracedAudio_demo
{
    public class RADemo_performanceTest : MonoBehaviour
    {
        //Playing 30 sounds at once takes ~6ms, 0.2ms per sound, not too good. Pooling may help?
        //Updating 240 sounds properties takes 0.4ms, too good to be true?
        [SerializeField] private float delay = 0.1f;
        [SerializeField] private int soundCount = 3;
        [SerializeField] private AudioReference aaudio = new();
        private float timer = 0.0f;
        private readonly AudioProps props = new();

        // Update is called once per frame
        private void Update()
        {
            timer += Time.deltaTime;
            props.pos = transform.position;

            if (timer > delay)
            {
                timer = 0.0f;

                for (int i = 0; i < soundCount; i++)
                {
                    var a = aaudio.Play(props);
                    //aaudio.Stop(a);
                }
            }
        }
    }
}
