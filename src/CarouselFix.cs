using System;
using UnityEngine;

namespace LooplerCarLoader;

// Runs Patches.FixPickers every frame; the car picker has no Update of its own to hook.
public class CarouselFix : MonoBehaviour
{
    public CarouselFix(IntPtr ptr) : base(ptr) { }

    void Update() => Patches.FixPickers();
}
