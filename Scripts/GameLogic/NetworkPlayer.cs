using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class NetworkPlayer : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> Alias = new NetworkVariable<FixedString32Bytes>();
    public NetworkVariable<Color32> ColorJugador = new NetworkVariable<Color32>();
    public NetworkVariable<int> TropasDisponibles = new NetworkVariable<int>(0);

    public MyArray<Territory> Territorios { get; private set; } = new MyArray<Territory>(42);
    public ManoJugador Mano = new ManoJugador();

    [Header("Prefabs (asignar en prefab)")]
    public GameObject manoUIPrefab;
    [HideInInspector] public ManoJugadorUI localManoUI;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Debug.Log($"NetworkPlayer spawn OwnerClientId={OwnerClientId} IsOwner={IsOwner}");

        if (IsOwner)
        {
            RequestSetAliasAndColorServerRpc(GameSettings.NombreJugador, Random.ColorHSV());
        }

        Alias.OnValueChanged += (_, __) => Debug.Log($"Alias actualizado: {Alias.Value.ToString()} (ClientId={OwnerClientId})");

        if (IsOwner && manoUIPrefab != null)
        {
            var go = Instantiate(manoUIPrefab);
            localManoUI = go.GetComponent<ManoJugadorUI>();
            if (localManoUI != null) localManoUI.Initialize(this);
        }

        // Intentar registrar en board (a veces BoardManager aún no creado)
        if (BoardManager.Instance != null)
            BoardManager.Instance.RegistrarJugador(this);
        else
            Invoke(nameof(TryRegistrarJugador), 0.5f);
    }

    private void TryRegistrarJugador()
    {
        if (BoardManager.Instance != null) BoardManager.Instance.RegistrarJugador(this);
        else Invoke(nameof(TryRegistrarJugador), 0.5f);
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestSetAliasAndColorServerRpc(string alias, Color color, ServerRpcParams rpcParams = default)
    {
        Alias.Value = alias;
        ColorJugador.Value = (Color32)color;
        Debug.Log($"(Server) Alias y color seteados para OwnerClientId={OwnerClientId}");
        BoardManager.Instance?.RegistrarJugador(this);
    }

    // Server: agregar carta a mano lógica de este jugador y notificar al owner
    public void AgregarCartaManoLogica_Server(Carta carta)
    {
        if (!IsServer) return;
        if (carta == null) return;
        Mano.AgregarCartaMano(carta);

        var clientRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { this.OwnerClientId } } };
        ShowObtainedCardClientRpc((carta.territorio != null) ? carta.territorio.Idx : -1, (int)carta.tipo, clientRpcParams);
    }

    [ClientRpc]
    private void ShowObtainedCardClientRpc(int territoryIdx, int tipoCode, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner && localManoUI != null) localManoUI.MostrarCartaObtenidaVisual(territoryIdx, (CardType)tipoCode);
        else
        {
            var manoUI = FindFirstObjectByType<ManoJugadorUI>();
            if (manoUI != null) manoUI.MostrarCartaObtenidaVisual(territoryIdx, (CardType)tipoCode);
            else Debug.LogWarning("ShowObtainedCardClientRpc: no se encontró ManoJugadorUI local.");
        }
    }

    // Request from owner -> server to canjear
    [ServerRpc(RequireOwnership = true)]
    public void RequestCanjearServerRpc(CardSelection selection, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        MyArray<Carta> selec = new MyArray<Carta>(3);
        List<int> usedIndices = new List<int>();
        int[] wantedIdxs = new int[] { selection.idx0, selection.idx1, selection.idx2 };
        int[] wantedTipos = new int[] { selection.tipo0, selection.tipo1, selection.tipo2 };

        for (int req = 0; req < 3; req++)
        {
            int wantedIdx = wantedIdxs[req];
            CardType wantedTipo = (CardType)wantedTipos[req];
            bool found = false;
            for (int j = 0; j < Mano.mano.Count; j++)
            {
                if (usedIndices.Contains(j)) continue;
                Carta c = Mano.mano[j];
                int cIdx = (c.territorio != null) ? c.territorio.Idx : -1;
                if (cIdx == wantedIdx && c.tipo == wantedTipo)
                {
                    selec.Add(c);
                    usedIndices.Add(j);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                var cpFail = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { this.OwnerClientId } } };
                NotifyCanjeResultClientRpc(false, 0, new RemovedCardsInfo(), cpFail);
                return;
            }
        }

        int n_change_local = Mazo.CartasIntercambiadas;
        ulong tropas = 0;
        try { tropas = Mano.Canjear(selec, ref n_change_local); }
        catch (System.Exception ex)
        {
            Debug.LogError("Mano.Canjear excepción: " + ex.Message);
            var cpErr = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { this.OwnerClientId } } };
            NotifyCanjeResultClientRpc(false, 0, new RemovedCardsInfo(), cpErr);
            return;
        }

        if (tropas == 0)
        {
            var cpZero = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { this.OwnerClientId } } };
            NotifyCanjeResultClientRpc(false, 0, new RemovedCardsInfo(), cpZero);
            return;
        }

        if (BoardManager.Instance != null && BoardManager.Instance.mazo != null) BoardManager.Instance.mazo.AgregarCarta(selec);

        Mazo.CartasIntercambiadas = n_change_local;
        int tropasInt = (int)tropas;
        TropasDisponibles.Value += tropasInt;

        RemovedCardsInfo removed = new RemovedCardsInfo();
        removed.count = selec.Count;
        removed.idx0 = removed.idx1 = removed.idx2 = -1;
        removed.tipo0 = removed.tipo1 = removed.tipo2 = 0;
        for (int i = 0; i < selec.Count; i++)
        {
            if (i == 0) { removed.idx0 = selec[i].territorio != null ? selec[i].territorio.Idx : -1; removed.tipo0 = (int)selec[i].tipo; }
            if (i == 1) { removed.idx1 = selec[i].territorio != null ? selec[i].territorio.Idx : -1; removed.tipo1 = (int)selec[i].tipo; }
            if (i == 2) { removed.idx2 = selec[i].territorio != null ? selec[i].territorio.Idx : -1; removed.tipo2 = (int)selec[i].tipo; }
        }

        var clientRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { this.OwnerClientId } } };
        NotifyCanjeResultClientRpc(true, tropasInt, removed, clientRpcParams);
        Debug.Log($"Server: Canje válido para {OwnerClientId}. +{tropasInt} tropas.");
    }

    [ClientRpc]
    private void NotifyCanjeResultClientRpc(bool exito, int tropas, RemovedCardsInfo removed, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner && localManoUI != null) localManoUI.HandleCanjeResult(exito, tropas, removed);
        else
        {
            var manoUI = FindFirstObjectByType<ManoJugadorUI>();
            if (manoUI != null) manoUI.HandleCanjeResult(exito, tropas, removed);
            else Debug.LogWarning("NotifyCanjeResultClientRpc: no se encontró ManoJugadorUI en cliente.");
        }
    }

    // Asignar refuerzos server-side
    public void AsignarRefuerzos()
    {
        if (!IsServer) return;
        int tropasBase = Mathf.Max(3, Territorios.Count / 3);
        TropasDisponibles.Value += tropasBase;
        Debug.Log($"(Server) {Alias.Value.ToString()} recibe {tropasBase} tropas.");
    }

    // Recompensa por conquista: crear carta ligada al territorio y notificar al owner
    public void RecompensaConquista(Territory terr)
    {
        if (!IsServer) return;
        if (Mano.mano.Count >= 7) { Debug.Log($"⚠️ {Alias.Value.ToString()} tiene 7 cartas."); return; }
        var nueva = Carta.CrearAleatoriaDesdeTerritorio(terr);
        Mano.AgregarCartaMano(nueva);
        // Notificar owner
        var clientRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { this.OwnerClientId } } };
        ShowObtainedCardClientRpc((nueva.territorio != null) ? nueva.territorio.Idx : -1, (int)nueva.tipo, clientRpcParams);
    }

    public bool ComprobarTerritorio(Territory terr) => Territorios.Contains(terr);

    public void AgregarTerritorio(Territory terr)
    {
        if (!Territorios.Contains(terr)) { Territorios.Add(terr); terr.UpdateColor(); }
    }

    public void EliminarTerritorio(Territory terr)
    {
        if (Territorios.Contains(terr)) { Territorios.Remove(terr); terr.UpdateColor(); }
    }

    // Opcional: Request hand snapshot (owner pide su mano completa)
    [ServerRpc(RequireOwnership = true)]
    public void RequestHandServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        var client = rpcParams.Receive.SenderClientId;
        CardData[] arr = new CardData[Mano.mano.Count];
        for (int i = 0; i < Mano.mano.Count; i++)
        {
            arr[i].territoryIdx = (Mano.mano[i].territorio != null) ? Mano.mano[i].territorio.Idx : -1;
            arr[i].tipo = (int)Mano.mano[i].tipo;
        }
        var clientRpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { client } } };
        SendHandClientRpc(arr, clientRpcParams);
    }

    [ClientRpc]
    private void SendHandClientRpc(CardData[] hand, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner && localManoUI != null) localManoUI.RecibirManoDesdeServer(hand);
        else
        {
            var manoUI = FindFirstObjectByType<ManoJugadorUI>();
            if (manoUI != null) manoUI.RecibirManoDesdeServer(hand);
            else Debug.LogWarning("SendHandClientRpc: no se encontró ManoJugadorUI en cliente.");
        }
    }
}
