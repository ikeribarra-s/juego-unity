using System.Text;
using UnityEngine;

/// <summary>Dev probe (not in scenes): logs blink closures, checks step-frame rule and write budget for one SkullBlink.</summary>
[DefaultExecutionOrder(500)]
public class SkullBlinkProbe : MonoBehaviour
{
    public SkullBlink Target;
    public RetroAnimationStepper Stepper;
    public readonly System.Collections.Generic.List<float> CloseTimes = new();
    public int Doubles, OffStepChanges, Frames, GcFrames;
    float _prevL, _prevR; bool _closed; float _startTime; int _startWrites; int _closesInSeq;
    long _gc;

    void Start() { _startTime = Time.time; _startWrites = Target.WriteCount; }
    void LateUpdate()
    {
        Frames++;
        float l = Target.WeightL, r = Target.WeightR;
        if ((!Mathf.Approximately(l, _prevL) || !Mathf.Approximately(r, _prevR)) && Stepper != null
            && Stepper.LastStepFrame != Time.frameCount && l != 0f && r != 0f) OffStepChanges++;
        bool closed = l > 90f || r > 90f;
        if (closed && !_closed) CloseTimes.Add(Time.time);
        _closed = closed; _prevL = l; _prevR = r;
    }
    public string Report()
    {
        var sb = new StringBuilder();
        float dur = Time.time - _startTime;
        sb.Append($"dur={dur:F1}s closes={CloseTimes.Count} offStepChanges={OffStepChanges} writes/s={(Target.WriteCount - _startWrites) / dur:F2}\ngaps:");
        for (int i = 1; i < CloseTimes.Count; i++) sb.Append($" {CloseTimes[i] - CloseTimes[i - 1]:F2}");
        return sb.ToString();
    }
}
