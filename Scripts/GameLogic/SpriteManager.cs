using UnityEngine;
using System.Collections.Generic;

public static class SpriteManager
{
    //crea un diccionario para almacenar el tipo de carta junto con su sprite correspondiente
    private static Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    //metodo que me devuelve el sprite correspondiente de una carta
    public static Sprite GetSpriteForCarta(Carta c)
    {
        if (c == null) return null; //si la carta es nula entonces edevuelve nulo
        
        string key = TipoToResourceName(c.tipo); //ve cual es el tipo de carta y lo convierte a string

        //si el key es nulo o vacion entonces retonorna un sprite nulo
        if (string.IsNullOrEmpty(key)) return null;

        //si la llave esta dentro del diccionario entonces devuelve el sprite correspondiente
        if (cache.TryGetValue(key, out Sprite s)) return s;
        
        //variable encargada de guardar el Load del sprite tipo de carta
        Sprite loaded = Resources.Load<Sprite>("Cartas/" + key);

        /*
        Ya que el tipo de carta existe
        y el tipo de carta no se encuentra dentro del
        diccionario de los sprites.
        Entonces verificamos que el sprite exista
        si existe entonces lo metemos dentro del diccionario
        */
        if (loaded != null) cache[key] = loaded;
        
        else Debug.LogWarning($"Sprite no encontrado en Resources/Cartas/{key}");
        
        return loaded; //retorna el sprite
    }

    //metodo para obtener el string correspondiennte el tipo de carta
    private static string TipoToResourceName(CardType t) //recibe in tipo de carta t
    {
        //se hace un caso por cada tipo de carta, para devolver el tipo de carta pero en string
        switch (t)
        {
            case CardType.Infanteria: return "infanteria";
            case CardType.Caballeria: return "caballeria";
            case CardType.Artilleria: return "artilleria";
            default: return null;
        }
    }
}
