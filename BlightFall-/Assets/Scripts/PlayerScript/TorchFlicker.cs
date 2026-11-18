using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Light))]
public class TorchFlicker : MonoBehaviour
{
    public static readonly List<TorchFlicker> ActiveTorches = new List<TorchFlicker>();
    public Light TorchLight { get; private set; }

    [Header("Flicker Settings")]
    [SerializeField] private float minIntensity = 2.4f;
    [SerializeField] private float maxIntensity = 3.2f;
    [SerializeField] private float flickerSpeed = 3.5f;

    private float seed;

    private void Awake()
    {
        TorchLight = GetComponent<Light>();
        seed = Random.Range(0f, 100f);
    }

    private void OnEnable()
    {
        if (!ActiveTorches.Contains(this))
            ActiveTorches.Add(this);
    }

    private void OnDisable()
    {
        ActiveTorches.Remove(this);
    }

    private void Update()
    {
        float noise = Mathf.PerlinNoise(seed, Time.time * flickerSpeed);
        TorchLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, noise);
    }
}