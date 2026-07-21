using UnityEngine;

//HeadMaterialSetup - builds a material for the head shell at runtime.
//
//Exists so the head has a sensible appearance without hand-configuring a material asset,
//and so surface CHARACTER can be driven by the track later (Part 3): the same shell
//rendered as pale plaster, dark stone, or translucent glass depending on what the music
//measures as.
//
//The shell is rendered DOUBLE-SIDED. When the head is cut open the camera sees the inside
//face of the far wall, and with default backface culling that wall would be invisible -
//you would look through the back of the skull into empty space. Disabling culling makes
//the cavity read as an enclosed volume, which is the whole point of hollowing it.

[RequireComponent(typeof(HeadField))]
public class HeadMaterialSetup : MonoBehaviour
{
    public enum Finish
    {
        Plaster,      //matte pale - the anatomical model look//
        Stone,        //darker, rougher//
        Polished,     //smooth and reflective//
        Translucent   //glassy, lets the interior show through//
    }

    [Header("Appearance")]
    [SerializeField] private Finish finish = Finish.Plaster;
    [SerializeField] private Color tint = new Color(0.82f, 0.80f, 0.76f);
    [SerializeField, Range(0f, 1f)] private float alpha = 1f;

    [Header("Wireframe Debug")]
    //Renders the shell as flat unlit colour, useful when checking form rather than shading.
    [SerializeField] private bool unlit = false;

    private Material _material;

    private void Awake()
    {
        Build();
        var field = GetComponent<HeadField>();
        if (field != null) field.SetMaterial(_material);
    }

    private void Build()
    {
        Shader shader = unlit
            ? Shader.Find("Universal Render Pipeline/Unlit")
            : Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null) shader = Shader.Find("Standard");

        _material = new Material(shader);

        Color c = tint;
        c.a = alpha;
        _material.color = c;

        if (!unlit)
        {
            switch (finish)
            {
                case Finish.Plaster:
                    SetFloatIfPresent("_Smoothness", 0.15f);
                    SetFloatIfPresent("_Metallic", 0f);
                    break;
                case Finish.Stone:
                    SetFloatIfPresent("_Smoothness", 0.08f);
                    SetFloatIfPresent("_Metallic", 0f);
                    _material.color = c * 0.55f;
                    break;
                case Finish.Polished:
                    SetFloatIfPresent("_Smoothness", 0.85f);
                    SetFloatIfPresent("_Metallic", 0.35f);
                    break;
                case Finish.Translucent:
                    SetFloatIfPresent("_Smoothness", 0.9f);
                    SetFloatIfPresent("_Metallic", 0f);
                    MakeTransparent(0.35f);
                    break;
            }
        }

        if (alpha < 1f && finish != Finish.Translucent) MakeTransparent(alpha);

        //Double-sided: the interior wall must be visible when the head is cut open.//
        SetFloatIfPresent("_Cull", 0f);
        _material.doubleSidedGI = true;
    }

    private void MakeTransparent(float a)
    {
        _material.SetFloat("_Surface", 1f);
        _material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _material.SetFloat("_ZWrite", 0f);
        _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _material.renderQueue = 3000;

        Color c = _material.color;
        c.a = a;
        _material.color = c;
    }

    private void SetFloatIfPresent(string prop, float value)
    {
        if (_material.HasProperty(prop)) _material.SetFloat(prop, value);
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
