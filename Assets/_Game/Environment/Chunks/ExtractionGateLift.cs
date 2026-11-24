using System.Collections;
using UnityEngine;

/// <summary>
/// CH_Extraction_Gate: lifts the portcullis (<c>SM_Extraction_Gate_Gate</c> + its collider child) along +Y when
/// <see cref="MissionManager.GeneratorsCompleted"/> fires — "the way out is open". Added by the generator.
/// </summary>
[DisallowMultipleComponent]
public class ExtractionGateLift : MonoBehaviour
{
    [SerializeField] private Transform _gate;
    [SerializeField] private float _liftHeight = 4f;
    [SerializeField] private float _duration = 2.5f;
    [SerializeField] private SoundDefinition _openSound;

    private bool _subscribed, _open;

    public void Setup(Transform gate) => _gate = gate;

    private void OnEnable() => TrySubscribe();
    private void Start() => TrySubscribe();

    private void TrySubscribe()
    {
        if (_subscribed || MissionManager.Instance == null) return;
        MissionManager.Instance.GeneratorsCompleted += Open;
        _subscribed = true;
    }

    private void OnDisable()
    {
        if (_subscribed && MissionManager.Instance != null) MissionManager.Instance.GeneratorsCompleted -= Open;
        _subscribed = false;
    }

    [ContextMenu("Open Gate")]
    public void Open()
    {
        if (_open || _gate == null) return;
        _open = true;
        if (_openSound != null && SoundManager.Instance != null) SoundManager.Instance.Play(_openSound, _gate.position);
        StartCoroutine(Lift());
    }

    private IEnumerator Lift()
    {
        Vector3 from = _gate.localPosition, to = from + Vector3.up * _liftHeight;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, _duration))
        {
            _gate.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        _gate.localPosition = to;
    }
}
