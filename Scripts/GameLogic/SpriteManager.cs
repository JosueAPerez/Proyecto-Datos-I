using UnityEngine;
using System.Collections.Generic;

// Simple manager que carga Sprites desde Resources/Cartas/...
public static class SpriteManager
{
    private static Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite GetSpriteForCarta(Carta c)
    {
        if (c == null) return null;
        string key = TipoToResourceName(c.tipo);
        if (string.IsNullOrEmpty(key)) return null;

        if (cache.TryGetValue(key, out Sprite s)) return s;

        Sprite loaded = Resources.Load<Sprite>("Cartas/" + key);
        if (loaded != null) cache[key] = loaded;
        else Debug.LogWarning($"Sprite no encontrado en Resources/Cartas/{key}");

        return loaded;
    }

    private static string TipoToResourceName(CardType t)
    {
        switch (t)
        {
            case CardType.Infanteria: return "infanteria";
            case CardType.Caballeria: return "caballeria";
            case CardType.Artilleria: return "artilleria";
            default: return null;
        }
    }
}
