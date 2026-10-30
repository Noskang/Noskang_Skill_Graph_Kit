using System;
using UnityEngine;

namespace Noskang.SkillGraph
{
    // Animation events named "SkillKeyframe" mark poses that skill data can refer to ('@node:Name').
    // The data tools read them from the .anim file; at run time they are only markers, so this receiver
    // keeps Unity from warning about a missing method and lets a host listen if it wants to.
    // A host that renames the function in its settings writes a receiver with the same method name.
    [DisallowMultipleComponent]
    public sealed class SkillKeyframeReceiver : MonoBehaviour
    {
        public const string DefaultFunction="SkillKeyframe";
        public event Action<string> Reached;
        public void SkillKeyframe(string name)=>Reached?.Invoke(name);
    }
}
