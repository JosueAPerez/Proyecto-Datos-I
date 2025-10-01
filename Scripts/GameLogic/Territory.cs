using UnityEngine;
using System.Collections.Generic;

public class Territory : MonoBehaviour
{
    [Header("Identity")]
    public int Idx;
    public string TerritoryName;

    [Header("Gameplay State")]
    public NetworkPlayer Owner = null;
    public int Soldiers = 0;

    [Header("Visuals")]
    public Color NeutralColor = Color.gray;
    [HideInInspector] public Color PlayerColor; // 👈 mantenemos esto para BoardManager

    [Header("Neighbors")]
    public List<Territory> Neighbors = new List<Territory>();

    private SpriteRenderer rend;

    private void Awake()
    {
        rend = GetComponent<SpriteRenderer>();
        if (rend == null)
            Debug.LogError($"❌ Missing SpriteRenderer en {name}");
    }

    private void Start()
    {
        // si no hay dueño => neutral
        if (Owner == null)
            PlayerColor = NeutralColor;
        else
            PlayerColor = Owner.ColorJugador.Value;

        UpdateColor();
    }

    private void OnMouseDown()
    {
        Debug.Log($"Clicked on {TerritoryName} (Idx={Idx}, Owner={(Owner != null ? Owner.Alias.Value.ToString() : "None")}, Soldiers={Soldiers})");

        if (rend != null)
            rend.color = Color.yellow;

        BoardManager.Instance?.OnTerritoryClicked(this);
    }

    public void AddNeighbor(Territory neighbor)
    {
        if (neighbor == null || neighbor == this || Neighbors.Contains(neighbor)) return;
        Neighbors.Add(neighbor);
    }

    public void UpdateColor()
    {
        if (rend == null) return;

        // sincronizamos PlayerColor según Owner
        if (Owner == null)
            PlayerColor = NeutralColor;
        else
            PlayerColor = Owner.ColorJugador.Value;

        rend.color = PlayerColor;
    }

    public void AddSoldiers(int amount) => Soldiers += amount;

    public void RemoveSoldiers(int amount) => Soldiers = Mathf.Max(0, Soldiers - amount);

    public void ResolveBattle(Territory defender)
    {
        if (this.Soldiers > defender.Soldiers)
        {
            Debug.Log($"{TerritoryName} conquered {defender.TerritoryName}");
            defender.Owner = this.Owner;
            defender.UpdateColor();

            defender.Soldiers = this.Soldiers - 1;
            this.Soldiers = 1;
        }
        else
        {
            Debug.Log($"{defender.TerritoryName} defended successfully");
            this.Soldiers = 1;
        }
    }
}