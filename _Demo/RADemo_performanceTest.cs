using UnityEngine;
using RaytracedAudio;

namespace RaytracedAudio_demo
{
    public class RADemo_performanceTest : MonoBehaviour
    {
        [SerializeField] private float delay = 0.1f;
        [SerializeField] private int soundCount = 3;
        [SerializeField] private AudioReference aaudio = new();
        private float timer = 0.0f;
        private readonly AudioProps props = new();

        private void OnEnable()
        {
            timer = delay;
        }

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
