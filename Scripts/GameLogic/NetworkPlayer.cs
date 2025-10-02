using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class Territory : NetworkBehaviour
{
    [Header("Identity")]
    public int Idx;
    public string TerritoryName;

    [Header("State (Networked)")]
    public NetworkVariable<ulong> TerritoryOwnerClientId = new NetworkVariable<ulong>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Soldiers = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Color> PlayerColorNet = new NetworkVariable<Color>(Color.gray, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Visuals")]
    public Color NeutralColor = Color.gray;
    [HideInInspector] public Color PlayerColor;

    [Header("Neighbors")]
    public List<Territory> Neighbors = new List<Territory>();

    private SpriteRenderer rend;
    private bool isHighlighted = false;

    private void Awake()
    {
        rend = GetComponent<SpriteRenderer>();
        if (rend == null) Debug.LogError($"Missing SpriteRenderer en {name}");
        else PlayerColor = rend.color;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        UpdateColor();
        PlayerColorNet.OnValueChanged += (_, newVal) => UpdateColor(newVal);
        TerritoryOwnerClientId.OnValueChanged += (_, __) => UpdateColor();
        Soldiers.OnValueChanged += (_, __) => { /* efectos visuales si quieres */ };
    }

    private void Start()
    {
        UpdateColor();
    }

    private void OnMouseDown()
    {
        BoardManager.Instance?.OnTerritoryClicked(this);
    }

    public void Highlight(bool state)
    {
        if (rend == null) return;
        if (state && !isHighlighted) { rend.color = Color.Lerp(PlayerColor, Color.yellow, 0.5f); isHighlighted = true; }
        else if (!state && isHighlighted) { rend.color = PlayerColor; isHighlighted = false; }
    }

    public void HighlightRed(bool state)
    {
        if (rend == null) return;
        if (state && !isHighlighted) { rend.color = Color.Lerp(PlayerColor, Color.red, 0.5f); isHighlighted = true; }
        else if (!state && isHighlighted) { rend.color = PlayerColor; isHighlighted = false; }
    }

    public void AddNeighbor(Territory neighbor)
    {
        if (neighbor == null || neighbor == this || Neighbors.Contains(neighbor)) return;
        Neighbors.Add(neighbor);
    }

    public void UpdateColor()
    {
        if (rend == null) return;
        if (TerritoryOwnerClientId.Value == 0) PlayerColor = NeutralColor;
        else PlayerColor = PlayerColorNet.Value;
        rend.color = PlayerColor;
    }

    private void UpdateColor(Color c)
    {
        PlayerColor = c;
        if (rend != null) rend.color = c;
    }

    // Métodos server-only
    public void SetOwnerServer(ulong clientId, Color color)
    {
        if (!IsServer) return;
        TerritoryOwnerClientId.Value = clientId;
        PlayerColorNet.Value = color;
    }

    public void SetSoldiersServer(int amount)
    {
        if (!IsServer) return;
        Soldiers.Value = Mathf.Max(0, amount);
    }

    public void AddSoldiersServer(int amount)
    {
        if (!IsServer) return;
        Soldiers.Value += amount;
    }

    public void RemoveSoldiersServer(int amount)
    {
        if (!IsServer) return;
        Soldiers.Value = Mathf.Max(0, Soldiers.Value - amount);
    }
}
