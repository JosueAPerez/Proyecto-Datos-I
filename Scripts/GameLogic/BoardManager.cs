// File: BoardManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class BoardManager : NetworkBehaviour
{
    public static BoardManager Instance;

    [Header("Board")]
    public Territory[] Territories;

    // Fases de turno
    public enum TurnPhase { Refuerzo, Ataque, Reagrupacion }
    public NetworkVariable<TurnPhase> FaseActual = new NetworkVariable<TurnPhase>(TurnPhase.Refuerzo);

    // Jugadores conectados (referencias a NetworkObject)
    private NetworkList<NetworkObjectReference> jugadoresConectados;

    // Map fast access
    public Dictionary<int, Territory> territoryById = new Dictionary<int, Territory>();

    // Control de turnos
    public NetworkVariable<int> JugadorActualIdx = new NetworkVariable<int>(0);

    // Mazo / Cartas (lógica está en Mazo/ManoJugador)
    public Mazo mazo;

    // Fichas globales
    public int contadorGlobalIntercambios = 0;

    // ---------- ATAQUE PENDING SYSTEM ----------
    private enum PendingState { Waiting, Resolving, Resolved, Cancelled }

    private class AttackPending
    {
        public int AttackId;
        public int AtacanteIdx;
        public int DefensorIdx;
        public int TropasAtacantesRequested; // cantidad inicial que el atacante comprometió (1..3)
        public ulong AttackerClientId;
        public ulong DefenderClientId;
        public float TimeCreated;
        public PendingState State = PendingState.Waiting;

        public AttackPending(int id, int atkIdx, int defIdx, int tropasAtk, ulong attackerId, ulong defenderId, float timeCreated)
        {
            AttackId = id;
            AtacanteIdx = atkIdx;
            DefensorIdx = defIdx;
            TropasAtacantesRequested = tropasAtk;
            AttackerClientId = attackerId;
            DefenderClientId = defenderId;
            TimeCreated = timeCreated;
            State = PendingState.Waiting;
        }
    }

    private int nextAttackId = 1;
    private readonly Dictionary<int, AttackPending> pendingAttacks = new Dictionary<int, AttackPending>();
    [Tooltip("Segundos para que el defensor responda; si expira, se usa la defensa máxima posible.")]
    public float defenderResponseTimeout = 10f;

    // ---------- selección / UI helpers ----------
    private Territory ultimoSeleccionado;
    private Territory atacanteSeleccionado;
    private Territory reagrupacionOrigen;

    // Awake / Init
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        if (jugadoresConectados == null)
            jugadoresConectados = new NetworkList<NetworkObjectReference>();

        Territories = FindObjectsByType<Territory>(FindObjectsSortMode.None);
        Array.Sort(Territories, (a, b) => a.Idx.CompareTo(b.Idx));

        foreach (var t in Territories)
        {
            if (territoryById.ContainsKey(t.Idx))
                Debug.LogWarning($"⚠️ Duplicate Idx: {t.Idx} en {t.name}");
            else
                territoryById[t.Idx] = t;
        }

        LoadAdjacency();

        // Init mazo lógico (usar MyArray<Territory> para compat)
        MyArray<Territory> listaTerr = new MyArray<Territory>(Territories.Length);
        for (int i = 0; i < Territories.Length; i++) listaTerr.Add(Territories[i]);
        mazo = new Mazo(listaTerr);
        mazo.LlenarMazo();

        Debug.Log($"✅ BoardManager initialized with {territoryById.Count} territories. Mazo count: {Mazo.cartas.Count}");

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
        }

        JugadorActualIdx.OnValueChanged += (oldVal, newVal) => { Debug.Log($"🔄 Cambio de turno: {oldVal} -> {newVal}"); };
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        LogAndSync($"🔄 Escena cargada con Netcode: {sceneName}");

        foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            RegistrarJugador(player);
        }
    }

    // Registrar jugadores cuando spawnean
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

        if (!jugadoresConectados.Contains(reference))
        {
            jugadoresConectados.Add(reference);
            LogAndSync($"(Server) Jugador registrado: {p.Alias.Value} (ClientId={p.OwnerClientId})");
        }

        // Repartir territorios cuando haya al menos 2 jugadores (o la condición que prefieras)
        if (jugadoresConectados.Count >= 2)
        {
            RepartirTerritorios();
        }
    }

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

    private void RepartirTerritorios()
    {
        int totalPlayers = jugadoresConectados.Count;
        if (totalPlayers == 0) return;

        int territoriosPorJugador = Territories.Length / totalPlayers;
        int currentIdx = 0;

        foreach (var playerRef in jugadoresConectados)
        {
            var player = GetJugadorFromReference(playerRef);
            if (player == null) continue;

            for (int i = 0; i < territoriosPorJugador && currentIdx < Territories.Length; i++, currentIdx++)
            {
                var terr = Territories[currentIdx];
                terr.SetOwnerServer(player.OwnerClientId, player.ColorJugador.Value);
                terr.SetSoldiersServer(1);
                player.AgregarTerritorio(terr);
            }

            Debug.Log($"✅ {player.Alias.Value.ToString()} recibió {territoriosPorJugador} territorios.");
        }
    }

    private NetworkPlayer GetJugadorFromReference(NetworkObjectReference reference)
    {
        if (reference.TryGet(out NetworkObject netObj))
        {
            return netObj.GetComponent<NetworkPlayer>();
        }
        return null;
    }

    [Serializable]
    private class SerializationWrapper { public TerritoryEntry[] entries; }
    [Serializable]
    private class TerritoryEntry { public int id; public int[] neighbors; }

    // ---------- Click handling (UI / lógica de interacción) ----------
    public void OnTerritoryClicked(Territory t)
    {
        LogAndSync($"[Click] {t.TerritoryName} (Idx={t.Idx}, Owner={t.TerritoryOwnerClientId.Value}, Tropas={t.Soldiers.Value})");

        if (ultimoSeleccionado != null)
            ultimoSeleccionado.Highlight(false);

        // Limpia highlights previos
        foreach (var terr in Territories)
            terr.Highlight(false);

        // Resalta el seleccionado
        t.HighlightRed(true);

        // Resalta vecinos
        foreach (var vecino in t.Neighbors)
            vecino.Highlight(true);

        ultimoSeleccionado = t;

        var jugador = GetJugadorActual();
        if (jugador == null) return;

        UIManager.Instance?.RefrescarUI();

        switch (FaseActual.Value)
        {
            case TurnPhase.Refuerzo:
                if (t.TerritoryOwnerClientId.Value == jugador.OwnerClientId)
                    UIManager.Instance?.SeleccionarTerritorio(t);
                break;

            case TurnPhase.Ataque:
                if (atacanteSeleccionado == null)
                {
                    // seleccionar atacante (tuyo y con >1 tropa)
                    if (t.TerritoryOwnerClientId.Value == jugador.OwnerClientId && t.Soldiers.Value > 1)
                    {
                        atacanteSeleccionado = t;
                        LogAndSync($"⚔️ Seleccionado atacante: {t.TerritoryName}");
                    }
                }
                else
                {
                    // Si clickeás un vecino enemigo → iniciar proceso de ataque
                    if (atacanteSeleccionado.Neighbors.Contains(t) && t.TerritoryOwnerClientId.Value != jugador.OwnerClientId)
                    {
                        // Tomar valor del slider del cliente para la # de tropas/dados atacantes
                        int tropasAEnviar = 1;
                        if (UIManager.Instance != null && UIManager.Instance.tropasSlider != null)
                            tropasAEnviar = Mathf.Clamp((int)UIManager.Instance.tropasSlider.value, 1, 3);

                        // Llamada al servidor para iniciar ataque (servidor validará todo)
                        ResolverAtaqueServerRpc(atacanteSeleccionado.Idx, t.Idx, tropasAEnviar);
                        atacanteSeleccionado = null;
                    }
                    else
                    {
                        LogAndSync("❌ Selección inválida para ataque.");
                        atacanteSeleccionado = null;
                    }
                }
                break;

            case TurnPhase.Reagrupacion:
                // Reagrupación (mover tropas) - igual que antes
                if (reagrupacionOrigen == null)
                {
                    if (t.TerritoryOwnerClientId.Value == jugador.OwnerClientId && t.Soldiers.Value > 1)
                    {
                        reagrupacionOrigen = t;
                        LogAndSync($"🔁 Origen reagrupación seleccionado: {t.TerritoryName}");
                        UIManager.Instance?.SeleccionarTerritorio(reagrupacionOrigen);
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
                    if (reagrupacionOrigen.Neighbors.Contains(t) && t.TerritoryOwnerClientId.Value == jugador.OwnerClientId)
                    {
                        int maxMovible = Mathf.Max(0, reagrupacionOrigen.Soldiers.Value - 1);
                        if (maxMovible <= 0)
                        {
                            LogAndSync($"❌ No hay tropas movibles en {reagrupacionOrigen.TerritoryName}.");
                            reagrupacionOrigen = null;
                            UIManager.Instance?.RefrescarUI();
                            break;
                        }

                        int cantidad = 1;
                        if (UIManager.Instance != null && UIManager.Instance.tropasSlider != null)
                            cantidad = (int)UIManager.Instance.tropasSlider.value;

                        cantidad = Mathf.Clamp(cantidad, 1, maxMovible);

                        MoverTropasServerRpc(reagrupacionOrigen.Idx, t.Idx, cantidad);
                        LogAndSync($"🔁 Solicitud de mover {cantidad} tropas de {reagrupacionOrigen.TerritoryName} a {t.TerritoryName} enviada al servidor.");
                        UIManager.Instance?.RefrescarUI();
                        reagrupacionOrigen = null;
                    }
                    else
                    {
                        LogAndSync("❌ Selección inválida para reagrupación (debe ser vecino y suyo).");
                        reagrupacionOrigen = null;
                        UIManager.Instance?.RefrescarUI();
                    }
                }
                break;
        }
    }

    // ---------- Turnos / fases ----------
    public void IniciarTurno()
    {
        if (!IsServer) return;
        if (jugadoresConectados.Count == 0) return;

        var jugador = GetJugadorFromReference(jugadoresConectados[JugadorActualIdx.Value]);
        if (jugador == null) return;

        jugador.AsignarRefuerzos();
        FaseActual.Value = TurnPhase.Refuerzo;

        LogAndSync($"🎯 Turno de {jugador.Alias.Value}, fase: {FaseActual.Value}");
        UIManager.Instance?.RefrescarUI();
    }

    [ServerRpc(RequireOwnership = false)]
    public void CambiarFaseServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        var siguiente = (TurnPhase)(((int)FaseActual.Value + 1) % Enum.GetValues(typeof(TurnPhase)).Length);
        FaseActual.Value = siguiente;

        LogAndSync($"🔄 Fase cambiada a: {FaseActual.Value}");
        UIManager.Instance?.RefrescarUI();
    }

    [ServerRpc(RequireOwnership = false)]
    public void TerminarTurnoServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (jugadoresConectados.Count == 0) return;

        JugadorActualIdx.Value = (JugadorActualIdx.Value + 1) % jugadoresConectados.Count;
        IniciarTurno();
        UIManager.Instance?.RefrescarUI();
    }

    public NetworkPlayer GetJugadorActual()
    {
        if (jugadoresConectados.Count == 0) return null;
        return GetJugadorFromReference(jugadoresConectados[JugadorActualIdx.Value]);
    }

    public NetworkPlayer GetJugadorPorClientId(ulong clientId)
    {
        foreach (var j in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            if (j.OwnerClientId == clientId) return j;
        return null;
    }

    public void IncrementarIntercambioGlobal()
    {
        contadorGlobalIntercambios++;
    }

    // ---------- ATAQUE: RPCs y resolución ----------

    // Cliente llama para iniciar ataque (pide X tropas/dados)
    [ServerRpc(RequireOwnership = false)]
    private void ResolverAtaqueServerRpc(int atacanteIdx, int defensorIdx, int tropasAtacantesRequested, ServerRpcParams rpcParams = default)
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

        // Prompt only to defender
        var clientRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { defenderClientId } } };
        DefenderPromptClientRpc(id, atacanteIdx, defensorIdx, tropasAtacantesRequested, clientRpcParams);

        // Start timeout coroutine to auto-choose defense if defender doesn't respond
        StartCoroutine(WaitForDefenderResponseCoroutine(id, defenderResponseTimeout));

        return id;
    }

    [ClientRpc]
    private void DefenderPromptClientRpc(int attackId, int atacanteIdx, int defensorIdx, int tropasAtacantesRequested, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[Client] DefenderPrompt recibida: attackId={attackId}, atacanteIdx={atacanteIdx}, defensorIdx={defensorIdx}, atkRequested={tropasAtacantesRequested}");

        // La UI cliente debe mostrar un panel para elegir 1..min(2, tropas en su territorio)
        // y luego llamar DefenderResponseServerRpc(attackId, chosenDefenseDice);
    }

    private IEnumerator WaitForDefenderResponseCoroutine(int attackId, float timeout)
    {
        yield return new WaitForSeconds(timeout);

        if (!pendingAttacks.TryGetValue(attackId, out AttackPending pending)) yield break;
        if (pending.State != PendingState.Waiting) yield break;

        // Defender default: máxima posible (policy requerida)
        Territory defensor = null;
        territoryById.TryGetValue(pending.DefensorIdx, out defensor);

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

        if (pending.State != PendingState.Waiting)
        {
            LogAndSync($"❌ DefenderResponseServerRpc: pending attackId {attackId} en estado {pending.State} → ignorando.");
            return;
        }

        if (sender != pending.DefenderClientId)
        {
            LogAndSync($"❌ DefenderResponseServerRpc: client {sender} no es el defensor (esperado {pending.DefenderClientId}).");
            return;
        }

        if (!territoryById.TryGetValue(pending.DefensorIdx, out Territory defensor))
        {
            LogAndSync($"❌ DefenderResponseServerRpc: territorio defensor {pending.DefensorIdx} no encontrado.");
            pendingAttacks.Remove(attackId);
            return;
        }

        int maxDefPossible = Mathf.Min(2, Mathf.Max(1, defensor.Soldiers.Value));
        tropasDefensorElegidas = Mathf.Clamp(tropasDefensorElegidas, 1, maxDefPossible);

        ResolvePendingAttackWithDefenderChoice(attackId, tropasDefensorElegidas);
    }

    // Core: resuelve el pending usando la elección de defensa y repite tiradas hasta que:
    // - el atacante pierde todas las tropas que envió (pending.TropasAtacantesRequested -> 0), o
    // - el defensor queda sin tropas en el territorio (defensor.Soldiers == 0) -> conquista
    private void ResolvePendingAttackWithDefenderChoice(int attackId, int defenderChoice)
    {
        if (!pendingAttacks.TryGetValue(attackId, out AttackPending pending))
        {
            LogAndSync($"❌ ResolvePending: pending {attackId} no existe.");
            return;
        }

        pending.State = PendingState.Resolving;

        if (!territoryById.TryGetValue(pending.AtacanteIdx, out Territory atacante) ||
            !territoryById.TryGetValue(pending.DefensorIdx, out Territory defensor))
        {
            LogAndSync($"❌ ResolvePending: territorios no válidos para attackId {attackId}. Cancelando.");
            pending.State = PendingState.Cancelled;
            pendingAttacks.Remove(attackId);
            return;
        }

        // Revalidar ownership
        if (atacante.TerritoryOwnerClientId.Value != pending.AttackerClientId)
        {
            LogAndSync($"❌ ResolvePending: ownership atacante cambió. Cancelando attackId={attackId}.");
            pending.State = PendingState.Cancelled;
            pendingAttacks.Remove(attackId);
            return;
        }
        if (defensor.TerritoryOwnerClientId.Value != pending.DefenderClientId)
        {
            LogAndSync($"❌ ResolvePending: ownership defensor cambió. Cancelando attackId={attackId}.");
            pending.State = PendingState.Cancelled;
            pendingAttacks.Remove(attackId);
            return;
        }

        // Variables de combate
        int attackerRemaining = pending.TropasAtacantesRequested; // tropas que el atacante comprometió y que lucharán hasta morir
        int defenderChosen = Mathf.Clamp(defenderChoice, 1, 2); // preferencia inicial del defensor

        // Ejecutar rounds repetidos hasta que uno se quede sin tropas comprometidas/territorio
        // Nota: en cada round, el numero de dados disponibles puede cambiar (no pueden tirar más dados que tropas vivas)
        while (attackerRemaining > 0 && defensor.Soldiers.Value > 0)
        {
            int attackerDiceThisRound = Mathf.Min(3, attackerRemaining, Mathf.Max(1, atacante.Soldiers.Value - 0)); // attackerRemaining asegura que no pase de lo enviado
            int defenderDiceThisRound = Mathf.Min(defenderChosen, defensor.Soldiers.Value);

            // Sanity clamp
            attackerDiceThisRound = Mathf.Clamp(attackerDiceThisRound, 1, 3);
            defenderDiceThisRound = Mathf.Clamp(defenderDiceThisRound, 1, 2);

            // Tiradas (servidor)
            List<int> atkRolls = new List<int>();
            List<int> defRolls = new List<int>();
            for (int i = 0; i < attackerDiceThisRound; i++) atkRolls.Add(UnityEngine.Random.Range(1, 7));
            for (int i = 0; i < defenderDiceThisRound; i++) defRolls.Add(UnityEngine.Random.Range(1, 7));
            atkRolls.Sort((a, b) => b - a); // descendente
            defRolls.Sort((a, b) => b - a);

            int comparisons = Mathf.Min(atkRolls.Count, defRolls.Count);
            int attackerLoss = 0, defenderLoss = 0;
            for (int i = 0; i < comparisons; i++)
            {
                if (atkRolls[i] > defRolls[i]) defenderLoss++;
                else attackerLoss++;
            }

            // Aplicar pérdidas (server-only) - restar tropas tanto en territorio como en el contador attackerRemaining
            // Asegurar no negativos
            attackerLoss = Mathf.Clamp(attackerLoss, 0, atacante.Soldiers.Value - 1); // nunca quitar la última tropa del territorio (la base)
            defensorLossClamp:
            defenderLoss = Mathf.Clamp(defenderLoss, 0, defensor.Soldiers.Value);

            // Aplicar
            atacante.RemoveSoldiersServer(attackerLoss);
            defensor.RemoveSoldiersServer(defenderLoss);

            attackerRemaining = Mathf.Max(0, attackerRemaining - attackerLoss);

            LogAndSync($"🎲 Round attackId={attackId} atkDice={attackerDiceThisRound} defDice={defenderDiceThisRound} atkRolls=[{string.Join(",", atkRolls)}] defRolls=[{string.Join(",", defRolls)}] => atkLoss={attackerLoss} defLoss={defenderLoss} (atkRem={attackerRemaining}, defLeft={defensor.Soldiers.Value})");

            // Notificar resultados parciales a todos (opcional)
            var allParams = new ClientRpcParams();
            AttackRoundResultClientRpc(attackId, attackerDiceThisRound, defenderDiceThisRound, atkRolls.ToArray(), defRolls.ToArray(), attackerLoss, defenderLoss, attackerRemaining, defensor.Soldiers.Value, allParams);

            // Si defensor queda en 0 -> conquista y terminamos
            if (defensor.Soldiers.Value == 0)
            {
                // mover tropas vivas del atacante al nuevo territorio (al menos 1, no dejar 0 en origen)
                int maxMovible = Mathf.Max(1, atacante.Soldiers.Value - 1);
                int tropasMover = Mathf.Clamp(attackerRemaining, 1, maxMovible);

                defensor.SetOwnerServer(atacante.TerritoryOwnerClientId.Value, atacante.PlayerColorNet.Value);
                defensor.SetSoldiersServer(tropasMover);
                atacante.RemoveSoldiersServer(tropasMover);

                // Notificar conquista (final)
                var allParams2 = new ClientRpcParams();
                AttackResultClientRpc(true, pending.AtacanteIdx, pending.DefensorIdx, 0 /*totalAtkLoss not tracked globally*/, 0 /*totalDefLoss*/, tropasMover, allParams2);

                LogAndSync($"🏴 Conquista: attackId={attackId} {atacante.TerritoryName} conquistó {defensor.TerritoryName} moviendo {tropasMover} tropas.");
                break;
            }

            // Si attackerRemaining == 0 -> atacante quedó sin tropas enviadas -> termina sin conquista
            if (attackerRemaining <= 0)
            {
                var allParams3 = new ClientRpcParams();
                AttackResultClientRpc(false, pending.AtacanteIdx, pending.DefensorIdx, 0, 0, 0, allParams3);
                LogAndSync($"🛡️ Ataque finalizado: attackId={attackId} -> atacante perdió todas las tropas enviadas.");
                break;
            }

            // Si ninguno se acabó, el loop continúa (se repite la tirada) automáticamente con las tropas actuales.
            // Nota: el defensor no puede cambiar su elección inicial en esta implementación.
        }

        // limpiar pending
        pending.State = PendingState.Resolved;
        pendingAttacks.Remove(attackId);
    }

    // ClientRpc para resultados de cada round (opcional, para animaciones)
    [ClientRpc]
    private void AttackRoundResultClientRpc(int attackId, int atkDice, int defDice, int[] atkRolls, int[] defRolls, int atkLoss, int defLoss, int atkRemaining, int defRemaining, ClientRpcParams clientRpcParams = default)
    {
        // Los clientes pueden usar esto para reproducir animaciones por round
        Debug.Log($"[Client] AttackRoundResult attackId={attackId} atkLoss={atkLoss} defLoss={defLoss} atkRem={atkRemaining} defRem={defRemaining}");
    }

    // ClientRpc final de resultado de ataque
    [ClientRpc]
    private void AttackResultClientRpc(bool conquered, int atacanteIdx, int defensorIdx, int attackerLoss, int defenderLoss, int tropasMovidas, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[Client] AttackResult: conquered={conquered}, moved={tropasMovidas}");
        UIManager.Instance?.RefrescarUI();
    }

    // ---------- Mover tropas (reagrupación) ----------
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

        int maxMovible = Mathf.Max(0, origen.Soldiers.Value - 1); // debe quedar >=1
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

    // ---------- Eliminación de jugador (versión: devolver cartas al mazo) ----------
    public void HandlePlayerEliminated(NetworkPlayer eliminatedPlayer, NetworkPlayer eliminatorPlayer = null)
    {
        if (!IsServer) return;
        if (eliminatedPlayer == null) return;

        ulong eliminatedClientId = eliminatedPlayer.OwnerClientId;
        LogAndSync($"[Server] Procesando eliminación del jugador {eliminatedClientId} (alias={eliminatedPlayer.Alias.Value})");

        // 1) Devolver cartas del jugador eliminado al mazo (opción segura)
        try
        {
            MyArray<Carta> lista = new MyArray<Carta>(eliminatedPlayer.Mano.mano.Capacity);
            for (int i = 0; i < eliminatedPlayer.Mano.mano.Count; i++)
            {
                var c = eliminatedPlayer.Mano.mano[i];
                if (c != null) lista.Add(c);
            }

            eliminatedPlayer.Mano = new ManoJugador();

            if (lista.Count > 0 && this.mazo != null)
            {
                this.mazo.AgregarCarta(lista);
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
            if (p.AttackerClientId == eliminatedClientId || p.DefenderClientId == eliminatedClientId)
                toCancel.Add(kv.Key);
        }
        foreach (int id in toCancel)
        {
            pendingAttacks.Remove(id);
            Debug.Log($"[Server] Cancelado pending attack {id} por eliminación de jugador {eliminatedClientId}");
        }

        // 4) Remover al jugador de jugadoresConectados y ajustar JugadorActualIdx
        int removedIndex = -1;
        for (int i = 0; i < jugadoresConectados.Count; i++)
        {
            if (jugadoresConectados[i].TryGet(out NetworkObject no))
            {
                var np = no.GetComponent<NetworkPlayer>();
                if (np != null && np.OwnerClientId == eliminatedClientId)
                {
                    removedIndex = i;
                    jugadoresConectados.RemoveAt(i);
                    break;
                }
            }
        }

        if (jugadoresConectados.Count == 0)
        {
            LogAndSync("[Server] No quedan jugadores activos tras la eliminación.");
        }
        else
        {
            if (removedIndex >= 0)
            {
                if (removedIndex < JugadorActualIdx.Value)
                {
                    JugadorActualIdx.Value = Mathf.Max(0, JugadorActualIdx.Value - 1);
                }
                else if (removedIndex == JugadorActualIdx.Value)
                {
                    if (JugadorActualIdx.Value >= jugadoresConectados.Count)
                        JugadorActualIdx.Value = 0;
                    IniciarTurno();
                }
            }
        }

        // 5) Reasignar territorios a neutral y limpiar tropas
        foreach (var t in Territories)
        {
            if (t.TerritoryOwnerClientId.Value == eliminatedClientId)
            {
                t.SetOwnerServer(0, t.NeutralColor);
                t.SetSoldiersServer(0);
            }
        }

        // 6) Sincronizar UI
        SyncAllClientsOnPlayerEliminatedClientRpc(eliminatedClientId);

        // 7) Comprobar victoria
        if (jugadoresConectados.Count == 1)
        {
            NetworkPlayer ganador = null;
            if (jugadoresConectados[0].TryGet(out NetworkObject no))
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

    // ---------- Utilidades y logging ----------
    public NetworkPlayer GetJugadorFromReference(NetworkObjectReference reference)
    {
        if (reference.TryGet(out NetworkObject netObj))
            return netObj.GetComponent<NetworkPlayer>();
        return null;
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
}




