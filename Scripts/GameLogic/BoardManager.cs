// BoardManager.cs
// Responsable de la lógica global del tablero, turnos, ataques pendientes, reparto de territorios, y sincronización Netcode.
// Notas: se añadieron helpers GetLocalPlayer() y GetTerritoryByIdx(), y se renombraron variables para consistencia.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class BoardManager : NetworkBehaviour
{
    // Singleton accesible desde otras clases
    public static BoardManager Instance;

    [Header("Board")]
    // Array con todos los territorios en escena (se obtiene en Awake)
    public Territory[] territories;

    // Fases de juego
    public enum Phase { Reinforcement = 0, Attack = 1, Regroup = 2 }

    // Fase actual sincronizada en red
    public NetworkVariable<Phase> currentPhase = new NetworkVariable<Phase>(Phase.Reinforcement);

    // Lista de jugadores conectados (referencias a NetworkObject) — se inicializa en OnNetworkSpawn
    private NetworkList<NetworkObjectReference> connectedPlayers;

    // Acceso rápido por id
    public Dictionary<int, Territory> territoryById = new Dictionary<int, Territory>();

    // Índice del jugador actual (en connectedPlayers)
    public NetworkVariable<int> currentPlayerIndex = new NetworkVariable<int>(0);

    // Mazo de cartas (lógica en Mazo)
    public Mazo deck;

    // Contador global de intercambios
    public int globalExchangeCount = 0;

    // --- Pending attack system ---
    private enum PendingState { Waiting, Resolving, Resolved, Cancelled }

    private class AttackPending
    {
        public int id;
        public int attackerTerritoryIdx;
        public int defenderTerritoryIdx;
        public int attackerTroopsRequested; // tropas que el atacante comprometió
        public ulong attackerClientId;
        public ulong defenderClientId;
        public float timeCreated;
        public PendingState state = PendingState.Waiting;

        public AttackPending(int id, int atkIdx, int defIdx, int tropasAtk, ulong attackerId, ulong defenderId, float timeCreated)
        {
            this.id = id;
            attackerTerritoryIdx = atkIdx;
            defenderTerritoryIdx = defIdx;
            attackerTroopsRequested = tropasAtk;
            attackerClientId = attackerId;
            defenderClientId = defenderId;
            this.timeCreated = timeCreated;
            state = PendingState.Waiting;
        }
    }

    private int nextAttackId = 1;
    private readonly Dictionary<int, AttackPending> pendingAttacks = new Dictionary<int, AttackPending>();

    [Tooltip("Segundos para que el defensor responda; si expira, se usa la defensa máxima posible.")]
    public float defenderResponseTimeout = 10f;

    // UI / selección
    private Territory lastSelectedTerritory;
    private Territory selectedAttackerTerritory;
    private Territory regroupOriginTerritory;

    // Awake: inicialización básica (no red)
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        // Obtener territorios en escena y crear mapa id -> territory
        territories = FindObjectsByType<Territory>(FindObjectsSortMode.None);
        Array.Sort(territories, (a, b) => a.Idx.CompareTo(b.Idx));
        territoryById.Clear();
        foreach (var t in territories)
        {
            if (t == null) continue;
            if (territoryById.ContainsKey(t.Idx)) Debug.LogWarning($"⚠️ Duplicate Idx: {t.Idx} en {t.name}");
            else territoryById[t.Idx] = t;
        }

        LoadAdjacency();

        // Inicializar mazo usando MyArray para compatibilidad
        MyArray<Territory> listaTerr = new MyArray<Territory>(territories.Length);
        for (int i = 0; i < territories.Length; i++) listaTerr.Add(territories[i]);
        deck = new Mazo(listaTerr);
        deck.LlenarMazo();

        Debug.Log($"✅ BoardManager initialized with {territoryById.Count} territories. Mazo count: {deck.GetCount()}");

        // Registrar evento de carga de escenas (si NetManager existe)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
        }

        // Log al cambiar jugador actual
        currentPlayerIndex.OnValueChanged += (oldVal, newVal) => { Debug.Log($"🔄 Cambio de turno: {oldVal} -> {newVal}"); };
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
    }

    // OnNetworkSpawn: inicializar NetworkList en servidor
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && connectedPlayers == null)
            connectedPlayers = new NetworkList<NetworkObjectReference>();
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        LogAndSync($"🔄 Escena cargada con Netcode: {sceneName}");
        foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            RegistrarJugador(player);
        }
    }

    // Registro de jugadores (servidor)
    public void RegistrarJugador(NetworkPlayer p)
    {
        if (!IsServer)
        {
            LogAndSync("RegistrarJugador llamado en cliente — ignorado.");
            return;
        }

        if (p == null || p.NetworkObject == null)
        {
            LogAndSync("⚠️ Intento de registrar jugador nulo o sin NetworkObject.");
            return;
        }

        var reference = new NetworkObjectReference(p.NetworkObject);

        if (connectedPlayers == null) connectedPlayers = new NetworkList<NetworkObjectReference>();

        if (!connectedPlayers.Contains(reference))
        {
            connectedPlayers.Add(reference);
            LogAndSync($"(Server) Jugador registrado: {p.Alias.Value} (ClientId={p.OwnerClientId})");
        }

        // Repartir territorios cuando haya al menos 2
        if (connectedPlayers.Count >= 2)
        {
            RepartirTerritorios();
        }
    }

    // Carga adyacencias desde Resources/adjacency.json (formato personalizado)
    private void LoadAdjacency()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>("adjacency");
        if (jsonFile == null)
        {
            LogAndSync("❌ adjacency.json no encontrado.");
            return;
        }

        var data = JsonUtility.FromJson<SerializationWrapper>(jsonFile.text);
        foreach (var entry in data.entries)
        {
            if (!territoryById.ContainsKey(entry.id)) continue;
            var current = territoryById[entry.id];
            foreach (int neighborId in entry.neighbors)
            {
                if (territoryById.TryGetValue(neighborId, out Territory neighbor))
                    current.AddNeighbor(neighbor);
            }
        }
    }

    // Repartir territorios de forma simple (una por jugador en orden)
    private void RepartirTerritorios()
    {
        int totalPlayers = connectedPlayers.Count;
        if (totalPlayers == 0) return;

        int territoriosPorJugador = territories.Length / totalPlayers;
        int currentIdx = 0;

        foreach (var playerRef in connectedPlayers)
        {
            var player = GetJugadorFromReference(playerRef);
            if (player == null) continue;

            for (int i = 0; i < territoriosPorJugador && currentIdx < territories.Length; i++, currentIdx++)
            {
                var terr = territories[currentIdx];
                terr.SetOwnerServer(player.OwnerClientId, player.ColorJugador.Value);
                terr.SetSoldiersServer(1);
                player.AgregarTerritorio(terr);
            }

            Debug.Log($"✅ {player.Alias.Value.ToString()} recibió {territoriosPorJugador} territorios.");
        }
    }

    // Helper para convertir NetworkObjectReference a NetworkPlayer
    public NetworkPlayer GetJugadorFromReference(NetworkObjectReference reference)
    {
        if (reference.TryGet(out NetworkObject netObj))
            return netObj.GetComponent<NetworkPlayer>();
        return null;
    }

    [Serializable]
    private class SerializationWrapper { public TerritoryEntry[] entries; }
    [Serializable]
    private class TerritoryEntry { public int id; public int[] neighbors; }

    // --- Click handling ---
    public void OnTerritoryClicked(Territory t)
    {
        if (t == null) return;
        LogAndSync($"[Click] {t.TerritoryName} (Idx={t.Idx}, Owner={t.TerritoryOwnerClientId.Value}, Tropas={t.Soldiers.Value})");

        // Limpiar highlights previos
        if (lastSelectedTerritory != null) lastSelectedTerritory.Highlight(false);
        foreach (var terr in territories) if (terr != null) terr.Highlight(false);

        // Highlight rojo visual
        t.HighlightRed(true);
        foreach (var vecino in t.Neighbors) vecino.Highlight(true);
        lastSelectedTerritory = t;

        var jugador = GetJugadorLocal();
        if (jugador == null) return;

        UIManager.Instance?.RefrescarUI();

        switch (currentPhase.Value)
        {
            case Phase.Reinforcement:
                if (t.TerritoryOwnerClientId.Value == jugador.OwnerClientId)
                    UIManager.Instance?.SeleccionarTerritorio(t);
                break;

            case Phase.Attack:
                if (selectedAttackerTerritory == null)
                {
                    if (t.TerritoryOwnerClientId.Value == jugador.OwnerClientId && t.Soldiers.Value > 1)
                    {
                        selectedAttackerTerritory = t;
                        LogAndSync($"⚔️ Seleccionado atacante: {t.TerritoryName}");
                    }
                }
                else
                {
                    if (selectedAttackerTerritory.Neighbors.Contains(t) && t.TerritoryOwnerClientId.Value != jugador.OwnerClientId)
                    {
                        int tropasAEnviar = 1;
                        if (UIManager.Instance != null && UIManager.Instance.tropasSlider != null)
                            tropasAEnviar = Mathf.Clamp((int)UIManager.Instance.tropasSlider.value, 1, 3);

                        // Llamar al server para iniciar ataque
                        ResolverAtaqueServerRpc(selectedAttackerTerritory.Idx, t.Idx, tropasAEnviar);
                        selectedAttackerTerritory = null;
                    }
                    else
                    {
                        LogAndSync("❌ Selección inválida para ataque.");
                        selectedAttackerTerritory = null;
                    }
                }
                break;

            case Phase.Regroup:
                if (regroupOriginTerritory == null)
                {
                    if (t.TerritoryOwnerClientId.Value == jugador.OwnerClientId && t.Soldiers.Value > 1)
                    {
                        regroupOriginTerritory = t;
                        LogAndSync($"🔁 Origen reagrupación seleccionado: {t.TerritoryName}");
                        UIManager.Instance?.SeleccionarTerritorio(regroupOriginTerritory);
                        UIManager.Instance?.RefrescarUI();
                    }
                    else
                    {
                        LogAndSync("❌ Debes seleccionar un territorio propio con más de 1 tropa para iniciar reagrupación.");
                        UIManager.Instance?.RefrescarUI();
                    }
                }
                else
                {
                    if (regroupOriginTerritory.Neighbors.Contains(t) && t.TerritoryOwnerClientId.Value == jugador.OwnerClientId)
                    {
                        int maxMovible = Mathf.Max(0, regroupOriginTerritory.Soldiers.Value - 1);
                        if (maxMovible <= 0)
                        {
                            LogAndSync($"❌ No hay tropas movibles en {regroupOriginTerritory.TerritoryName}.");
                            regroupOriginTerritory = null;
                            UIManager.Instance?.RefrescarUI();
                            break;
                        }

                        int cantidad = 1;
                        if (UIManager.Instance != null && UIManager.Instance.tropasSlider != null)
                            cantidad = (int)UIManager.Instance.tropasSlider.value;

                        cantidad = Mathf.Clamp(cantidad, 1, maxMovible);
                        MoverTropasServerRpc(regroupOriginTerritory.Idx, t.Idx, cantidad);
                        LogAndSync($"🔁 Solicitud de mover {cantidad} tropas de {regroupOriginTerritory.TerritoryName} a {t.TerritoryName} enviada al servidor.");
                        UIManager.Instance?.RefrescarUI();
                        regroupOriginTerritory = null;
                    }
                    else
                    {
                        LogAndSync("❌ Selección inválida para reagrupación (debe ser vecino y suyo).");
                        regroupOriginTerritory = null;
                        UIManager.Instance?.RefrescarUI();
                    }
                }
                break;
        }
    }

    // --- Turnos ---
    public void IniciarTurno()
    {
        if (!IsServer) return;
        if (connectedPlayers == null || connectedPlayers.Count == 0) return;

        var player = GetJugadorFromReference(connectedPlayers[currentPlayerIndex.Value]);
        if (player == null) return;

        player.AsignarRefuerzos();
        currentPhase.Value = Phase.Reinforcement;
        LogAndSync($"🎯 Turno de {player.Alias.Value}, fase: {currentPhase.Value}");
        UIManager.Instance?.RefrescarUI();
    }

    [ServerRpc(RequireOwnership = false)]
    public void CambiarFaseServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        var siguiente = (Phase)(((int)currentPhase.Value + 1) % Enum.GetValues(typeof(Phase)).Length);
        currentPhase.Value = siguiente;
        LogAndSync($"🔄 Fase cambiada a: {currentPhase.Value}");
        UIManager.Instance?.RefrescarUI();
    }

    [ServerRpc(RequireOwnership = false)]
    public void TerminarTurnoServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (connectedPlayers == null || connectedPlayers.Count == 0) return;
        currentPlayerIndex.Value = (currentPlayerIndex.Value + 1) % connectedPlayers.Count;
        IniciarTurno();
        UIManager.Instance?.RefrescarUI();
    }

    // Devuelve el jugador actual en turno (servidor o cliente)
    public NetworkPlayer GetJugadorActual()
    {
        if (connectedPlayers == null || connectedPlayers.Count == 0) return null;
        return GetJugadorFromReference(connectedPlayers[currentPlayerIndex.Value]);
    }

    // Devuelve NetworkPlayer por clientId
    public NetworkPlayer GetJugadorPorClientId(ulong clientId)
    {
        foreach (var j in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            if (j.OwnerClientId == clientId) return j;
        return null;
    }

    public void IncrementarIntercambioGlobal() { globalExchangeCount++; }

    // --- ATAQUE: RPCs y resolución ---
    [ServerRpc(RequireOwnership = false)]
    public void ResolverAtaqueServerRpc(int atacanteIdx, int defensorIdx, int tropasAtacantesRequested, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!territoryById.TryGetValue(atacanteIdx, out Territory atacante) || !territoryById.TryGetValue(defensorIdx, out Territory defensor))
        {
            LogAndSync($"❌ ResolverAtaque: territorios inválidos (atk={atacanteIdx}, def={defensorIdx}).");
            return;
        }

        if (atacante.TerritoryOwnerClientId.Value != senderClientId)
        {
            LogAndSync($"❌ ResolverAtaque: cliente {senderClientId} no es dueño del territorio atacante {atacanteIdx}.");
            return;
        }

        if (defensor.TerritoryOwnerClientId.Value == senderClientId)
        {
            LogAndSync($"❌ ResolverAtaque: no puedes atacar a tu propio territorio ({defensorIdx}).");
            return;
        }

        if (!atacante.Neighbors.Contains(defensor))
        {
            LogAndSync($"❌ ResolverAtaque: {atacanteIdx} y {defensorIdx} no son vecinos.");
            return;
        }

        int maxAtqAllowed = Mathf.Max(0, atacante.Soldiers.Value - 1);
        if (maxAtqAllowed <= 0)
        {
            LogAndSync($"❌ ResolverAtaque: no hay tropas movibles en {atacante.TerritoryName} para atacar.");
            return;
        }

        tropasAtacantesRequested = Mathf.Clamp(tropasAtacantesRequested, 1, Mathf.Min(3, maxAtqAllowed));

        int attackId = CreatePendingAttack(atacanteIdx, defensorIdx, tropasAtacantesRequested, senderClientId, defensor.TerritoryOwnerClientId.Value);

        LogAndSync($"📩 Ataque pedido id={attackId} de cliente {senderClientId} ({atacante.TerritoryName}) -> {defensor.TerritoryName}; tropas solicitadas={tropasAtacantesRequested}");
    }

    private int CreatePendingAttack(int atacanteIdx, int defensorIdx, int tropasAtacantesRequested, ulong attackerClientId, ulong defenderClientId)
    {
        int id = nextAttackId++;
        if (nextAttackId <= 0) nextAttackId = 1;

        var pending = new AttackPending(id, atacanteIdx, defensorIdx, tropasAtacantesRequested, attackerClientId, defenderClientId, Time.time);
        pendingAttacks[id] = pending;

        // Notificar sólo al defensor con ClientRpcParams target
        var clientRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { defenderClientId } } };
        DefenderPromptClientRpc(id, atacanteIdx, defensorIdx, tropasAtacantesRequested, clientRpcParams);

        // Iniciar timeout para auto-defender si no responde
        StartCoroutine(WaitForDefenderResponseCoroutine(id, defenderResponseTimeout));
        return id;
    }

    [ClientRpc]
    private void DefenderPromptClientRpc(int attackId, int atacanteIdx, int defensorIdx, int tropasAtacantesRequested, ClientRpcParams clientRpcParams = default)
    {
        // El cliente defensor debe mostrar UI para elegir 1..min(2, tropas)
        Debug.Log($"[Client] DefenderPrompt recibida: attackId={attackId}, atacanteIdx={atacanteIdx}, defensorIdx={defensorIdx}, atkRequested={tropasAtacantesRequested}");
        UIManager.Instance?.ShowDefensePrompt(attackId, atacanteIdx, defensorIdx, tropasAtacantesRequested,  Mathf.Min(2, Mathf.Max(1, GetTerritoryByIdx(defensorIdx)?.Soldiers.Value ?? 1)));
    }

    private IEnumerator WaitForDefenderResponseCoroutine(int attackId, float timeout)
    {
        yield return new WaitForSeconds(timeout);

        if (!pendingAttacks.TryGetValue(attackId, out AttackPending pending)) yield break;
        if (pending.state != PendingState.Waiting) yield break;

        Territory defensor = null;
        territoryById.TryGetValue(pending.defenderTerritoryIdx, out defensor);

        int maxDefPossible = 1;
        if (defensor != null) maxDefPossible = Mathf.Min(2, Mathf.Max(1, defensor.Soldiers.Value));

        LogAndSync($"⏱️ Timeout defensa (attackId={attackId}). Aplicando defensa máxima posible: {maxDefPossible}.");

        try
        {
            ResolvePendingAttackWithDefenderChoice(attackId, maxDefPossible);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error al resolver ataque por timeout id={attackId}: {ex}");
            pendingAttacks.Remove(attackId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void DefenderResponseServerRpc(int attackId, int tropasDefensorElegidas, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        if (!pendingAttacks.TryGetValue(attackId, out AttackPending pending))
        {
            LogAndSync($"❌ DefenderResponseServerRpc: pending attackId {attackId} no existe.");
            return;
        }

        if (pending.state != PendingState.Waiting)
        {
            LogAndSync($"❌ DefenderResponseServerRpc: pending attackId {attackId} en estado {pending.state} → ignorando.");
            return;
        }

        if (sender != pending.defenderClientId)
        {
            LogAndSync($"❌ DefenderResponseServerRpc: client {sender} no es el defensor (esperado {pending.defenderClientId}).");
            return;
        }

        if (!territoryById.TryGetValue(pending.defenderTerritoryIdx, out Territory defensor))
        {
            LogAndSync($"❌ DefenderResponseServerRpc: territorio defensor {pending.defenderTerritoryIdx} no encontrado.");
            pendingAttacks.Remove(attackId);
            return;
        }

        int maxDefPossible = Mathf.Min(2, Mathf.Max(1, defensor.Soldiers.Value));
        tropasDefensorElegidas = Mathf.Clamp(tropasDefensorElegidas, 1, maxDefPossible);

        ResolvePendingAttackWithDefenderChoice(attackId, tropasDefensorElegidas);
    }

    // Resuelve rounds hasta que atacante o defensor se queden sin tropas
    private void ResolvePendingAttackWithDefenderChoice(int attackId, int defenderChoice)
    {
        if (!pendingAttacks.TryGetValue(attackId, out AttackPending pending))
        {
            LogAndSync($"❌ ResolvePending: pending {attackId} no existe.");
            return;
        }

        pending.state = PendingState.Resolving;

        if (!territoryById.TryGetValue(pending.attackerTerritoryIdx, out Territory attacker) ||
            !territoryById.TryGetValue(pending.defenderTerritoryIdx, out Territory defender))
        {
            LogAndSync($"❌ ResolvePending: territorios no válidos para attackId {attackId}. Cancelando.");
            pending.state = PendingState.Cancelled;
            pendingAttacks.Remove(attackId);
            return;
        }

        // Revalidar ownership
        if (attacker.TerritoryOwnerClientId.Value != pending.attackerClientId)
        {
            LogAndSync($"❌ ResolvePending: ownership atacante cambió. Cancelando attackId={attackId}.");
            pending.state = PendingState.Cancelled;
            pendingAttacks.Remove(attackId);
            return;
        }
        if (defender.TerritoryOwnerClientId.Value != pending.defenderClientId)
        {
            LogAndSync($"❌ ResolvePending: ownership defensor cambió. Cancelando attackId={attackId}.");
            pending.state = PendingState.Cancelled;
            pendingAttacks.Remove(attackId);
            return;
        }

        // Variables de combate
        int attackerRemaining = pending.attackerTroopsRequested;
        int defenderChosen = Mathf.Clamp(defenderChoice, 1, 2);

        // Acumuladores opcionales para resultado total
        int totalAttackerLoss = 0;
        int totalDefenderLoss = 0;

        while (attackerRemaining > 0 && defender.Soldiers.Value > 0)
        {
            int maxDiceByOrigin = Mathf.Max(0, attacker.Soldiers.Value - 1); // no puede tirar la última tropa si queda solo 1
            int attackerDiceThisRound = Mathf.Min(3, Mathf.Min(attackerRemaining, maxDiceByOrigin));
            int defenderDiceThisRound = Mathf.Min(defenderChosen, defender.Soldiers.Value);

            // Asegurar límites mínimos (si attackerDiceThisRound == 0 significa que no puede tirar)
            attackerDiceThisRound = Mathf.Clamp(attackerDiceThisRound, 0, 3);
            defenderDiceThisRound = Mathf.Clamp(defenderDiceThisRound, 0, 2);

            List<int> atkRolls = new List<int>();
            List<int> defRolls = new List<int>();
            for (int i = 0; i < attackerDiceThisRound; i++) atkRolls.Add(UnityEngine.Random.Range(1, 7));
            for (int i = 0; i < defenderDiceThisRound; i++) defRolls.Add(UnityEngine.Random.Range(1, 7));
            atkRolls.Sort((a, b) => b - a);
            defRolls.Sort((a, b) => b - a);

            int comparisons = Mathf.Min(atkRolls.Count, defRolls.Count);
            int attackerLoss = 0, defenderLoss = 0;
            for (int i = 0; i < comparisons; i++)
            {
                if (atkRolls[i] > defRolls[i]) defenderLoss++;
                else attackerLoss++;
            }

            int maxAttLoss = Mathf.Max(0, attacker.Soldiers.Value - 1);
            attackerLoss = Mathf.Clamp(attackerLoss, 0, maxAttLoss);
            defenderLoss = Mathf.Clamp(defenderLoss, 0, defender.Soldiers.Value);

            // Aplicar pérdidas
            if (attackerLoss > 0) attacker.RemoveSoldiersServer(attackerLoss);
            if (defenderLoss > 0) defender.RemoveSoldiersServer(defenderLoss);

            attackerRemaining = Mathf.Max(0, attackerRemaining - attackerLoss);

            totalAttackerLoss += attackerLoss;
            totalDefenderLoss += defenderLoss;

            LogAndSync($"🎲 Round attackId={attackId} atkDice={attackerDiceThisRound} defDice={defenderDiceThisRound} atkRolls=[{string.Join(",", atkRolls)}] defRolls=[{string.Join(",", defRolls)}] => atkLoss={attackerLoss} defLoss={defenderLoss} (atkRem={attackerRemaining}, defLeft={defender.Soldiers.Value})");

            // Notificar resultados parciales a todos
            var allParams = new ClientRpcParams();
            AttackRoundResultClientRpc(attackId, attackerDiceThisRound, defenderDiceThisRound, atkRolls.ToArray(), defRolls.ToArray(), attackerLoss, defenderLoss, attackerRemaining, defender.Soldiers.Value, allParams);

            // Si defensor queda en 0 -> conquista
            if (defender.Soldiers.Value == 0)
            {
                int maxMovible = Mathf.Max(1, attacker.Soldiers.Value - 1);
                int tropasMover = Mathf.Clamp(attackerRemaining, 1, maxMovible);

                defender.SetOwnerServer(attacker.TerritoryOwnerClientId.Value, attacker.PlayerColorNet.Value);
                defender.SetSoldiersServer(tropasMover);
                attacker.RemoveSoldiersServer(tropasMover);

                var allParams2 = new ClientRpcParams();
                AttackResultClientRpc(true, pending.attackerTerritoryIdx, pending.defenderTerritoryIdx, totalAttackerLoss, totalDefenderLoss, tropasMover, allParams2);

                LogAndSync($"🏴 Conquista: attackId={attackId} {attacker.TerritoryName} conquistó {defender.TerritoryName} moviendo {tropasMover} tropas.");
                break;
            }

            if (attackerRemaining <= 0)
            {
                var allParams3 = new ClientRpcParams();
                AttackResultClientRpc(false, pending.attackerTerritoryIdx, pending.defenderTerritoryIdx, totalAttackerLoss, totalDefenderLoss, 0, allParams3);
                LogAndSync($"🛡️ Ataque finalizado: attackId={attackId} -> atacante perdió todas las tropas enviadas.");
                break;
            }
        }

        // limpiar pending
        pending.state = PendingState.Resolved;
        pendingAttacks.Remove(attackId);
    }

    [ClientRpc]
    private void AttackRoundResultClientRpc(int attackId, int atkDice, int defDice, int[] atkRolls, int[] defRolls, int atkLoss, int defLoss, int atkRemaining, int defRemaining, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[Client] AttackRoundResult attackId={attackId} atkLoss={atkLoss} defLoss={defLoss} atkRem={atkRemaining} defRem={defRemaining}");
        // Los clientes pueden usar esto para animaciones (UIManager lo escucha)
        var summary = new AttackResultSummary()
        {
            attackId = attackId,
            atacanteIdx = -1,
            defensorIdx = -1,
            attRolls = atkRolls,
            defRolls = defRolls,
            attackerLosses = atkLoss,
            defenderLosses = defLoss,
            conquered = false,
            tropasMovidas = 0
        };
        UIManager.Instance?.HandleAttackResult(summary);
    }

    [ClientRpc]
    private void AttackResultClientRpc(bool conquered, int atacanteIdx, int defensorIdx, int attackerLoss, int defenderLoss, int tropasMovidas, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[Client] AttackResult: conquered={conquered}, moved={tropasMovidas}");
        var summary = new AttackResultSummary()
        {
            attackId = -1,
            atacanteIdx = atacanteIdx,
            defensorIdx = defensorIdx,
            attRolls = new int[0],
            defRolls = new int[0],
            attackerLosses = attackerLoss,
            defenderLosses = defenderLoss,
            conquered = conquered,
            tropasMovidas = tropasMovidas
        };
        UIManager.Instance?.HandleAttackResult(summary);
        UIManager.Instance?.RefrescarUI();
    }

    // --- Mover tropas (reagrupación) ---
    [ServerRpc(RequireOwnership = false)]
    private void MoverTropasServerRpc(int origenIdx, int destinoIdx, int cantidad, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        var senderClientId = rpcParams.Receive.SenderClientId;
        if (!territoryById.TryGetValue(origenIdx, out Territory origen)) return;
        if (!territoryById.TryGetValue(destinoIdx, out Territory destino)) return;

        if (origen.TerritoryOwnerClientId.Value != senderClientId)
        {
            LogAndSync($"❌ MoverTropas: el origen (Idx {origenIdx}) no pertenece al cliente {senderClientId}.");
            return;
        }

        if (destino.TerritoryOwnerClientId.Value != senderClientId)
        {
            LogAndSync($"❌ MoverTropas: el destino (Idx {destinoIdx}) no pertenece al cliente {senderClientId}.");
            return;
        }

        if (!origen.Neighbors.Contains(destino))
        {
            LogAndSync($"❌ MoverTropas: origen y destino no son vecinos (Idx {origenIdx} -> {destinoIdx}).");
            return;
        }

        int maxMovible = Mathf.Max(0, origen.Soldiers.Value - 1);
        if (maxMovible <= 0)
        {
            LogAndSync($"❌ MoverTropas: no hay tropas movibles en {origen.TerritoryName}.");
            return;
        }

        int mover = Mathf.Clamp(cantidad, 1, maxMovible);
        origen.Soldiers.Value = Mathf.Max(1, origen.Soldiers.Value - mover);
        destino.Soldiers.Value += mover;

        LogAndSync($"🔁 {GetJugadorPorClientId(senderClientId)?.Alias.Value.ToString() ?? senderClientId.ToString()} movió {mover} tropas de {origen.TerritoryName} a {destino.TerritoryName}.");
        UIManager.Instance?.RefrescarUI();
    }

    // --- Eliminación de jugador ---
    public void HandlePlayerEliminated(NetworkPlayer eliminatedPlayer, NetworkPlayer eliminatorPlayer = null)
    {
        if (!IsServer) return;
        if (eliminatedPlayer == null) return;

        ulong eliminatedClientId = eliminatedPlayer.OwnerClientId;
        LogAndSync($"[Server] Procesando eliminación del jugador {eliminatedClientId} (alias={eliminatedPlayer.Alias.Value})");

        // 1) Devolver cartas del jugador eliminado al mazo (server-side)
        try
        {
            MyArray<Carta> lista = new MyArray<Carta>(eliminatedPlayer.Mano.hand.Capacity);
            for (int i = 0; i < eliminatedPlayer.Mano.hand.Count; i++)
            {
                var c = eliminatedPlayer.Mano.hand[i];
                if (c != null) lista.Add(c);
            }

            eliminatedPlayer.Mano = new ManoJugador();

            if (lista.Count > 0 && this.deck != null)
            {
                this.deck.AgregarCarta(lista);
                Debug.Log($"[Server] Devueltas {lista.Count} cartas del jugador {eliminatedClientId} al mazo.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[Server] Error al devolver cartas al mazo: " + ex.Message);
        }

        // 2) Marcar eliminado y notificar owner para activar spectator mode localmente
        eliminatedPlayer.IsEliminated.Value = true;
        var notifyParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { eliminatedClientId } } };
        eliminatedPlayer.NotifyEliminatedClientRpc(notifyParams);

        // 3) Cancelar pendings que involucren al jugador
        var toCancel = new List<int>();
        foreach (var kv in pendingAttacks)
        {
            var p = kv.Value;
            if (p.attackerClientId == eliminatedClientId || p.defenderClientId == eliminatedClientId)
                toCancel.Add(kv.Key);
        }
        foreach (int id in toCancel)
        {
            pendingAttacks.Remove(id);
            Debug.Log($"[Server] Cancelado pending attack {id} por eliminación de jugador {eliminatedClientId}");
        }

        // 4) Remover al jugador de connectedPlayers y ajustar currentPlayerIndex
        int removedIndex = -1;
        for (int i = 0; i < connectedPlayers.Count; i++)
        {
            if (connectedPlayers[i].TryGet(out NetworkObject no))
            {
                var np = no.GetComponent<NetworkPlayer>();
                if (np != null && np.OwnerClientId == eliminatedClientId)
                {
                    removedIndex = i;
                    connectedPlayers.RemoveAt(i);
                    break;
                }
            }
        }

        if (connectedPlayers.Count == 0)
        {
            LogAndSync("[Server] No quedan jugadores activos tras la eliminación.");
        }
        else
        {
            if (removedIndex >= 0)
            {
                if (removedIndex < currentPlayerIndex.Value)
                {
                    currentPlayerIndex.Value = Mathf.Max(0, currentPlayerIndex.Value - 1);
                }
                else if (removedIndex == currentPlayerIndex.Value)
                {
                    if (currentPlayerIndex.Value >= connectedPlayers.Count)
                        currentPlayerIndex.Value = 0;
                    IniciarTurno();
                }
            }
        }

        // 5) Reasignar territorios a neutral y limpiar tropas
        foreach (var t in territories)
        {
            if (t != null && t.TerritoryOwnerClientId.Value == eliminatedClientId)
            {
                t.SetOwnerServer(0, t.NeutralColor);
                t.SetSoldiersServer(0);
            }
        }

        // 6) Sincronizar UI
        SyncAllClientsOnPlayerEliminatedClientRpc(eliminatedClientId);

        // 7) Comprobar victoria
        if (connectedPlayers.Count == 1)
        {
            NetworkPlayer ganador = null;
            if (connectedPlayers[0].TryGet(out NetworkObject no))
                ganador = no.GetComponent<NetworkPlayer>();

            if (ganador != null)
            {
                Debug.Log($"[Server] ¡Victoria! Ganador: {ganador.Alias.Value.ToString()} ({ganador.OwnerClientId})");
                var allParams = new ClientRpcParams();
                EndGameClientRpc(ganador.OwnerClientId, allParams);
            }
        }

        LogAndSync($"[Server] Finalizado manejo de eliminación para {eliminatedClientId}");
    }

    [ClientRpc]
    private void SyncAllClientsOnPlayerEliminatedClientRpc(ulong eliminatedClientId)
    {
        Debug.Log($"[Client] SyncAllClientsOnPlayerEliminatedClientRpc -> eliminado: {eliminatedClientId}");
        UIManager.Instance?.RefrescarUI();
    }

    [ClientRpc]
    private void EndGameClientRpc(ulong winnerClientId, ClientRpcParams clientRpcParams = default)
    {
        var winner = GetJugadorPorClientId(winnerClientId);
        string winnerName = winner != null ? winner.Alias.Value.ToString() : winnerClientId.ToString();
        UIManager.Instance?.ShowEndGame(winnerClientId, winnerName);
    }

    // Helpers adicionales (usadas por UI y otros scripts)
    public NetworkPlayer GetJugadorLocal()
    {
        if (NetworkManager.Singleton == null) return null;
        return GetJugadorPorClientId(NetworkManager.Singleton.LocalClientId);
    }

    public Territory GetTerritoryByIdx(int idx)
    {
        territoryById.TryGetValue(idx, out Territory t);
        return t;
    }

    private void LogAndSync(string mensaje)
    {
        Debug.Log(mensaje);
        if (IsServer) SyncDebugClientRpc(mensaje);
    }

    [ClientRpc]
    private void SyncDebugClientRpc(string mensaje)
    {
        if (!IsServer) Debug.Log($"[ServerSync] {mensaje}");
    }

    // Estructura pública para que UI reciba resúmenes (no serializado para RPCs, solo local)
    public class AttackResultSummary
    {
        public int attackId;
        public int atacanteIdx;
        public int defensorIdx;
        public int[] attRolls;
        public int[] defRolls;
        public int attackerLosses;
        public int defenderLosses;
        public bool conquered;
        public int tropasMovidas;
    }
}
