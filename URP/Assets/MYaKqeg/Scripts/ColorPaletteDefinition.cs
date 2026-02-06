using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Sonosthesia/Color Palette", fileName = "ColorPalette")]
public class ColorPaletteDefinition : ScriptableObject
{
    [SerializeField]
    private List<Color> colors = new List<Color> { Color.white };

    public IReadOnlyList<Color> Colors => colors;
}
