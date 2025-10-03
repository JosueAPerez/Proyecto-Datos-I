using UnityEngine;

public enum CardType
{
    Infanteria = 0,
    Caballeria = 1,
    Infanteria = 2,
}

[System.Serializable]
public class Carta
{
    public CardType tipo { get; private set; }
    public Territory territorio { get; private set; }
    public int id { get; private set; }
    
    public Carta(CardType type, Territory territory)
    {
        this.territorio = territory;
        this.tipo = type;
        this.id = (territorio!= null) ? territorio.Idx : -1;
    }
    
    // Convierte a CardData (compacto) para envío por RPC con Netcode
    public CardData ToCardData()
    {
        CardData d = new CardData();
        d.territoryIdx = (territorio != null) ? territorio.Idx : -1;
        d.tipo = (int)tipo;
        return d;
    }

    // Igualdad por tipo + territory id (útil para búsquedas en mano)
    public override bool Equals(object obj)
    {
        if (obj is Carta other)
            return this.id == other.id && this.tipo == other.tipo;
        return false;
    }

    public override int GetHashCode()
    {
        return id.GetHashCode() ^ ((int)tipo).GetHashCode();
    }
}


