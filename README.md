# UnityVoxelAudio
Audio manager and voxel based realtime audio occlusion and reverb.
- Dynamic echo, muffling and environment-aware spatialization
- Simple API to play and configure sounds, most FMOD features can be accessed through the API.

(Add image or audio clip here later)

## Performance

Main thread cost for 200 looping sounds with reverb + occlusion:  Initial (Starting all sounds at once) ~1.26ms | Constant ~0.18ms

Computing reverb + occlusion will consume 1 worker thread almost constantly.

Normal FMOD cost + extra reverb and lowpass filter for every EventInstance.

## Instructions

**Requirements** (Likely works in other versions / render pipelines)
<ul>
<li>Unity 2023.2.20f1 (Built-in)</li>
<li>Burst 1.8.21</li>
<li>Collections 2.1.4</li>
<li>Mathematics 1.2.6</li>
<li>FMOD 2.3.7</li>
<li>UnityVoxelSystem LATEST: https://github.com/Zombie1111/UnityVoxelSystem?tab=readme-ov-file</li>
</ul>

**General Setup**

<ol>
  <li>Setup FMOD and UnityVoxelSystem</li>
  <li>Download and copy the <code>_Demo</code>, <code>Scripts</code> and <code>Resources</code> folders into an empty folder inside your <code>Assets</code> directory</li>
  <li>Open <code>Tools->Raytraced Audio->Edit Settings</code>code> and add your FMOD buses to the list</li>
</ol>

```c#
using VoxelAudio;
using UnityEngine;

namespace VoxelAudio_demo
{
    public class RADemo_simplestPlayer : MonoBehaviour
    {
        [SerializeField] private AudioReference audioRef = new();

        private void Start()
        {
            audioRef.Play(transform);
        }
    }
}
```

## Technical Details

Occlusion is computed through a flood-fill from listener position and storing distance + last directly visible voxel index.

Reverb is solved by getting surrounding surface materials through rays from each EventInstance and listener.
