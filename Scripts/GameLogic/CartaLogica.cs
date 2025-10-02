using UnityEngine;
using System;

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
}
