using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class BoardManager : NetworkBehaviour
{
    public static BoardManager Instance;
    public Territory[] Territories;

    public enum TurnPhase { Refuerzo, Ataque, Reagrupacion }
    public NetworkVariable<TurnPhase> FaseActual = new NetworkVariable<TurnPhase>(TurnPhase.Refuerzo);

    public Dictionary<int, Territory> territoryById = new Dictionary<int, Territory>();
    public NetworkList<NetworkObjectReference> jugadoresConectados;

    public NetworkVariable<int> JugadorActualIdx = new NetworkVariable<int>(0);

    public Mazo mazo;
    public int contadorGlobalIntercambios = 0;

    // Pending attacks
    private struct AttackPending
    {
        public int attackId;
        public int atacanteIdx;
        public int defensorIdx;
        public int tropasAtacantesRequested;
        public ulong attackerClientId;
        public ulong defenderClientId;
        public double timeCreated;
    }

    private int nextAttackId = 1;
    private Dictionary<int, AttackPending> pendingAttacks = new Dictionary<int, AttackPending>();
    private float defenderResponseTimeout = 10f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        jugadoresConectados = new NetworkList<NetworkObjectReference>();

        Territories = FindObjectsByType<Territory>(FindObjectsSortMode.None);
        Array.Sort(Territories, (a, b) => a.Idx.CompareTo(b.Idx));

        foreach (var t in Territories)
            if (!territoryById.ContainsKey(t.Idx)) territoryById[t.Idx] = t;

        LoadAdjacency();

        if (IsServer) InitializeMazoServer();

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
    }

    private void Update()
    {
        if (!IsServer) return;
        if (pendingAttacks.Count == 0) return;

        List<int> toResolve = null;
        double now = Time.timeAsDouble;
        foreach (var kv in pendingAttacks)
        {
            var p = kv.Value;
            if (now - p.timeCreated >= defenderResponseTimeout)
            {
                if (toResolve == null) toResolve = new List<int>();
                toResolve.Add(kv.Key);
            }
        }

        if (toResolve != null)
        {
            foreach (var id in toResolve)
            {
                if (pendingAttacks.TryGetValue(id, out AttackPending pend))
                {
                    Debug.LogWarning($"Defensor no respondió para attackId={id}, aplicando defensa por defecto.");
                    int maxDef = 0;
                    if (territoryById.TryGetValue(pend.defensorIdx, out Territory def)) maxDef = Mathf.Min(2, def.Soldiers.Value);
                    if (maxDef < 1) maxDef = 1;
                    ResolveAttackAndNotify(id, maxDef);
                }
            }
        }
    }

    #region Init / adjacency / reparto
    private void InitializeMazoServer()
    {
        try
        {
            MyArray<Territory> listaTerr = new MyArray<Territory>(Territories.Length);
            for (int i = 0; i < Territories.Length; i++) listaTerr.Add(Territories[i]);
            mazo = new Mazo(listaTerr);
            mazo.LlenarMazo();
            Debug.Log($"Mazo inicializado (Server).");
        }
        catch (Exception ex) { Debug.LogWarning("InitializeMazoServer: " + ex.Message); }
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None)) RegistrarJugador(player);
    }

    public void RegistrarJugador(NetworkPlayer p)
    {
        if (!IsServer) return;
        if (p == null || p.NetworkObject == null) return;

        var reference = new NetworkObjectReference(p.NetworkObject);
        if (!jugadoresConectados.Contains(reference)) jugadoresConectados.Add(reference);

        if (jugadoresConectados.Count >= 2)
        {
            RepartirTerritorios();
            JugadorActualIdx.Value = 0;
            IniciarTurno();
        }
    }

    private void LoadAdjacency()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>("adjacency");
        if (jsonFile == null) return;
        var data = JsonUtility.FromJson<SerializationWrapper>(jsonFile.text);
        foreach (var entry in data.entries)
        {
            if (!territoryById.ContainsKey(entry.id)) continue;
            var current = territoryById[entry.id];
            foreach (int neighborId in entry.neighbors)
                if (territoryById.TryGetValue(neighborId, out Territory neighbor)) current.AddNeighbor(neighbor);
        }
    }

    private void RepartirTerritorios()
    {
        if (!IsServer) return;
        int totalPlayers = jugadoresConectados.Count;
        if (totalPlayers == 0) return;
        int territoriosPorJugador = Territories.Length / totalPlayers;
        int currentIdx = 0;

        for (int p = 0; p < jugadoresConectados.Count; p++)
        {
            var playerRef = jugadoresConectados[p];
            if (!playerRef.TryGet(out NetworkObject netObj)) continue;
            var player = netObj.GetComponent<NetworkPlayer>();
            if (player == null) continue;

            for (int i = 0; i < territoriosPorJugador && currentIdx < Territories.Length; i++, currentIdx++)
            {
                var terr = Territories[currentIdx];
                terr.SetOwnerServer(player.OwnerClientId, (Color)player.ColorJugador.Value);
                terr.SetSoldiersServer(1);
                player.AgregarTerritorio(terr);
            }
        }
    }
    #endregion

    #region Turnos
    public void IniciarTurno()
    {
        if (!IsServer) return;
        if (jugadoresConectados.Count == 0) return;
        var playerRef = jugadoresConectados[JugadorActualIdx.Value];
        if (!playerRef.TryGet(out NetworkObject netObj)) return;
        var jugador = netObj.GetComponent<NetworkPlayer>();
        if (jugador == null) return;

        jugador.AsignarRefuerzos();
        FaseActual.Value = TurnPhase.Refuerzo;
        UIManager.Instance?.RefrescarUI();
    }
    #endregion

    #region Ataque seguro (client -> server -> prompt defensor -> resolve)
    [ServerRpc(RequireOwnership = false)]
    public void AttackRequestServerRpc(int atacanteIdx, int defensorIdx, int tropasAtacantesRequested, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        ulong sender = rpcParams.Receive.SenderClientId;

        if (!territoryById.TryGetValue(atacanteIdx, out Territory atacante) || !territoryById.TryGetValue(defensorIdx, out Territory defensor))
        {
            Debug.LogWarning("AttackRequest: territorio inválido.");
            return;
        }

        if (atacante.TerritoryOwnerClientId.Value != sender)
        {
            Debug.LogWarning($"AttackRequest: cliente {sender} no es dueño del atacante {atacanteIdx}");
            return;
        }

        if (!atacante.Neighbors.Contains(defensor))
        {
            Debug.LogWarning("AttackRequest: territorios no son vecinos.");
            return;
        }

        int maxAtqPossible = Mathf.Min(3, Mathf.Max(0, atacante.Soldiers.Value - 1));
        if (maxAtqPossible < 1)
        {
            Debug.LogWarning("AttackRequest: no hay tropas movibles.");
            return;
        }

        int tropasAtq = Mathf.Clamp(tropasAtacantesRequested, 1, maxAtqPossible);

        int attackId = nextAttackId++;
        AttackPending pending = new AttackPending
        {
            attackId = attackId,
            atacanteIdx = atacanteIdx,
            defensorIdx = defensorIdx,
            tropasAtacantesRequested = tropasAtq,
            attackerClientId = sender,
            defenderClientId = defensor.TerritoryOwnerClientId.Value,
            timeCreated = Time.timeAsDouble
        };

        pendingAttacks[attackId] = pending;

        int maxDefPossible = Mathf.Min(2, defensor.Soldiers.Value);
        if (maxDefPossible < 1) maxDefPossible = 1;

        var clientRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { pending.defenderClientId } } };
        DefenderPromptClientRpc(attackId, atacanteIdx, defensorIdx, tropasAtq, maxDefPossible, clientRpcParams);

        Debug.Log($"[Server] AttackRequest attackId={attackId} creado. esperando defensa.");
    }

    [ClientRpc]
    private void DefenderPromptClientRpc(int attackId, int atacanteIdx, int defensorIdx, int tropasAtq, int maxDefPossible, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[Client] DefenderPrompt attackId={attackId} atqIdx={atacanteIdx} defIdx={defensorIdx} tropasAtq={tropasAtq} maxDef={maxDefPossible}");
        UIManager.Instance?.ShowDefensePrompt(attackId, atacanteIdx, defensorIdx, tropasAtq, maxDefPossible);
    }

    [ServerRpc(RequireOwnership = false)]
    public void DefenderResponseServerRpc(int attackId, int tropasDefensorChosen, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        ulong sender = rpcParams.Receive.SenderClientId;

        if (!pendingAttacks.TryGetValue(attackId, out AttackPending pending))
        {
            Debug.LogWarning($"DefenderResponse: attackId {attackId} no encontrado.");
            return;
        }

        if (sender != pending.defenderClientId)
        {
            Debug.LogWarning($"DefenderResponse: cliente {sender} no es defensor esperado.");
            return;
        }

        if (!territoryById.TryGetValue(pending.atacanteIdx, out Territory atacante) || !territoryById.TryGetValue(pending.defensorIdx, out Territory defensor))
        {
            Debug.LogWarning("DefenderResponse: territorios ya no válidos.");
            pendingAttacks.Remove(attackId);
            return;
        }

        int maxDefPossible = Mathf.Min(2, defensor.Soldiers.Value);
        int defensas = Mathf.Clamp(tropasDefensorChosen, 1, Math.Max(1, maxDefPossible));

        int maxAtqPossible = Mathf.Min(3, Mathf.Max(0, atacante.Soldiers.Value - 1));
        if (maxAtqPossible < 1)
        {
            Debug.LogWarning("DefenderResponse: atacante no tiene tropas movibles.");
            pendingAttacks.Remove(attackId);
            return;
        }

        ResolveAttackAndNotify(attackId, defensas);
    }

    private void ResolveAttackAndNotify(int attackId, int defenders)
    {
        if (!pendingAttacks.TryGetValue(attackId, out AttackPending pending)) return;

        if (!territoryById.TryGetValue(pending.atacanteIdx, out Territory atacante) || !territoryById.TryGetValue(pending.defensorIdx, out Territory defensor))
        {
            pendingAttacks.Remove(attackId);
            return;
        }

        int attackerDice = Mathf.Min(3, pending.tropasAtacantesRequested);
        attackerDice = Mathf.Min(attackerDice, Mathf.Max(1, atacante.Soldiers.Value - 1));
        int defenderDice = Mathf.Min(2, defenders);
        defenderDice = Mathf.Min(defenderDice, Math.Max(1, defensor.Soldiers.Value));

        int[] attRolls = new int[attackerDice];
        int[] defRolls = new int[defenderDice];
        for (int i = 0; i < attackerDice; i++) attRolls[i] = UnityEngine.Random.Range(1, 7);
        for (int i = 0; i < defenderDice; i++) defRolls[i] = UnityEngine.Random.Range(1, 7);

        Array.Sort(attRolls); Array.Reverse(attRolls);
        Array.Sort(defRolls); Array.Reverse(defRolls);

        int comps = Math.Min(attackerDice, defenderDice);
        int attackerLosses = 0, defenderLosses = 0;
        for (int i = 0; i < comps; i++)
        {
            if (attRolls[i] > defRolls[i]) defenderLosses++;
            else attackerLosses++;
        }

        atacante.Soldiers.Value = Mathf.Max(1, atacante.Soldiers.Value - attackerLosses);
        defensor.Soldiers.Value = Mathf.Max(0, defensor.Soldiers.Value - defenderLosses);

        bool conquered = false;
        int tropasMovidas = 0;

        if (defensor.Soldiers.Value == 0)
        {
            defensor.TerritoryOwnerClientId.Value = atacante.TerritoryOwnerClientId.Value;
            defensor.PlayerColorNet.Value = atacante.PlayerColorNet.Value;

            int maxMovibleDespues = Mathf.Max(1, atacante.Soldiers.Value - 1);
            tropasMovidas = Mathf.Min(pending.tropasAtacantesRequested, maxMovibleDespues);
            tropasMovidas = Mathf.Max(1, tropasMovidas);

            defensor.Soldiers.Value = tropasMovidas;
            atacante.Soldiers.Value = Mathf.Max(1, atacante.Soldiers.Value - tropasMovidas);

            var jugadorGanador = GetJugadorPorClientId(atacante.TerritoryOwnerClientId.Value);
            if (jugadorGanador != null) jugadorGanador.RecompensaConquista(defensor);

            conquered = true;
        }

        var summary = new AttackResultSummary
        {
            attackId = attackId,
            atacanteIdx = pending.atacanteIdx,
            defensorIdx = pending.defensorIdx,
            attackerDice = attackerDice,
            defenderDice = defenderDice,
            attRolls = attRolls,
            defRolls = defRolls,
            attackerLosses = attackerLosses,
            defenderLosses = defenderLosses,
            conquered = conquered,
            tropasMovidas = tropasMovidas
        };

        pendingAttacks.Remove(attackId);
        AttackResultClientRpc(JsonUtility.ToJson(summary));
    }

    [ClientRpc]
    private void AttackResultClientRpc(string summaryJson, ClientRpcParams clientRpcParams = default)
    {
        AttackResultSummary summary = JsonUtility.FromJson<AttackResultSummary>(summaryJson);
        UIManager.Instance?.HandleAttackResult(summary);
        UIManager.Instance?.RefrescarUI();
    }
    #endregion

    #region helpers
    public NetworkPlayer GetJugadorPorClientId(ulong clientId)
    {
        foreach (var np in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None)) if (np.OwnerClientId == clientId) return np;
        return null;
    }

    public NetworkPlayer GetJugadorLocal()
    {
        foreach (var np in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None)) if (np.IsOwner) return np;
        return null;
    }

    public int GetFibonacciBonus()
    {
        int[] fib = { 2, 3 };
        List<int> seq = new List<int>(fib);
        while (seq.Count <= contadorGlobalIntercambios) seq.Add(seq[seq.Count - 1] + seq[seq.Count - 2]);
        return seq[contadorGlobalIntercambios];
    }

    public void IncrementarIntercambioGlobal() => contadorGlobalIntercambios++;

    [Serializable]
    private class SerializationWrapper { public TerritoryEntry[] entries; }
    [Serializable]
    private class TerritoryEntry { public int id; public int[] neighbors; }

    [Serializable]
    public class AttackResultSummary
    {
        public int attackId;
        public int atacanteIdx;
        public int defensorIdx;
        public int attackerDice;
        public int defenderDice;
        public int[] attRolls;
        public int[] defRolls;
        public int attackerLosses;
        public int defenderLosses;
        public bool conquered;
        public int tropasMovidas;
    }
    #endregion

    // utility para clientes/UI
    public Territory GetTerritoryByIdx(int idx)
    {
        territoryById.TryGetValue(idx, out Territory t);
        return t;
    }
}
