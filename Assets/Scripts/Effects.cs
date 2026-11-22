using UnityEngine;

// Effets de particules générés par le code (aucun asset nécessaire) : explosions, fumée, poussière.
public static class Effects
{
    private static Material particleMaterial;

    private static Material ParticleMaterial
    {
        get
        {
            if (particleMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("UI/Default");
                particleMaterial = new Material(shader) { mainTexture = SoftCircle(64) };
            }
            return particleMaterial;
        }
    }

    // Disque au bord flou, pour que les particules ne soient pas des carrés.
    private static Texture2D SoftCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        return tex;
    }

    public static void Explosion(Vector3 pos, float radius)
    {
        // Boule de feu
        Burst(pos, 30, radius * 0.35f, radius * 0.7f, new Vector2(radius * 0.4f, radius * 0.9f), new Vector2(0.25f, 0.5f), 0f,
              new Color(1f, 0.9f, 0.4f), new Color(1f, 0.35f, 0.05f), 0.4f);
        // Fumée qui monte
        Burst(pos + Vector3.up * radius * 0.2f, 18, radius * 0.4f, radius * 0.25f, new Vector2(radius * 0.5f, radius * 1.1f), new Vector2(1.2f, 2.2f), -0.05f,
              new Color(0.35f, 0.33f, 0.32f, 0.9f), new Color(0.6f, 0.6f, 0.6f, 0f), 0.2f);
        // Mottes de terre projetées
        Burst(pos, 30, radius * 0.2f, radius * 2.2f, new Vector2(radius * 0.05f, radius * 0.12f), new Vector2(0.6f, 1.1f), 1.5f,
              new Color(0.35f, 0.24f, 0.12f), new Color(0.25f, 0.18f, 0.1f), 0f);
        Flash(pos + Vector3.up * radius * 0.3f, radius * 3f);
    }

    public static void Dust(Vector3 pos, float radius)
    {
        Burst(pos, 25, radius, radius * 0.4f, new Vector2(radius * 0.5f, radius), new Vector2(1f, 2f), -0.02f,
              new Color(0.55f, 0.5f, 0.45f, 0.85f), new Color(0.7f, 0.68f, 0.65f, 0f), 0f);
    }

    private static void Burst(Vector3 pos, int count, float shapeRadius, float speed, Vector2 size, Vector2 lifetime,
                              float gravity, Color start, Color end, float upBias)
    {
        var go = new GameObject("FX");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.duration = lifetime.y;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = start;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = Mathf.Max(0.01f, shapeRadius);
        shape.rotation = new Vector3(-90f, 0f, 0f); // demi-sphère tournée vers le haut

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                     new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(start.a * 0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f + upBias));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = ParticleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ps.Play();
    }

    private static void Flash(Vector3 pos, float range)
    {
        var go = new GameObject("Flash");
        go.transform.position = pos;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.7f, 0.3f);
        light.range = range;
        light.intensity = 4f;
        go.AddComponent<FlashFade>();
    }
}
