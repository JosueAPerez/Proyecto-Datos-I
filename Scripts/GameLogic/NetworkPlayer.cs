using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;
using Unity.Collections;

public class NetworkPlayer : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> Alias = new NetworkVariable<FixedString32Bytes>();
    public NetworkVariable<Color32> ColorJugador = new NetworkVariable<Color32>();
    public NetworkVariable<int> TropasDisponibles = new NetworkVariable<int>(0);
    public MyArray<Territory> Territorios { get; private set; } = new MyArray<Territory>(42);
    public ManoJugador Mano = new ManoJugador();
    
    [Header("Prefabs (asignar en prefab Player)")]
    public GameObject manoUIPrefab;
    [HideInInspector] public ManoJugadorUI localManoUI;
    public override void OnNetworkSpawn()
    {
        Debug.Log($"🎮 NetworkPlayer spawn -> OwnerClientId={OwnerClientId}, IsOwner={IsOwner}");

        if (IsOwner)
        {
            Alias.Value = GameSettings.NombreJugador;
            ColorJugador.Value = UnityEngine.Random.ColorHSV();
        }

        // Escuchar cuando cambia el alias (para el servidor)
        Alias.OnValueChanged += (oldVal, newVal) =>
        {
            Debug.Log($"📝 Alias actualizado: {newVal.ToString()} (ClientId={OwnerClientId})");
            BoardManager.Instance?.RegistrarJugador(this);
        };

        // Intentar registrar de una vez
        if (BoardManager.Instance != null)
        {
            BoardManager.Instance.RegistrarJugador(this);
        }
        else
        {
            Debug.LogWarning("⚠️ BoardManager aún no existe, esperando...");
            Invoke(nameof(TryRegistrarJugador), 0.5f);
        }
    }

    private void TryRegistrarJugador()
    {
        if (BoardManager.Instance != null)
        {
            BoardManager.Instance.RegistrarJugador(this);
        }
        else
        {
            Invoke(nameof(TryRegistrarJugador), 0.5f);
        }
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestCanjearServerRpc(CardSelection selection, ServerRpcParams rpcParams = default)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // Reconstruir la selección server-side desde la mano para evitar trampas
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

        // Ejecutar el canje
        int n_change_local = Mazo.CartasIntercambiadas;
        ulong tropas = 0;
        try
        {
            tropas = Mano.Canjear(selec, ref n_change_local);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Mano.Canjear lanzó excepción: " + ex.Message);
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

        // Devolver cartas al mazo
        if (BoardManager.Instance != null && BoardManager.Instance.mazo != null)
        {
            BoardManager.Instance.mazo.AgregarCarta(selec);
        }
        else
        {
            Debug.LogWarning("RequestCanjearServerRpc: BoardManager o mazo es null; las cartas no pudieron devolverse al mazo.");
        }

        // Actualizar contador global y dar tropas
        Mazo.CartasIntercambiadas = n_change_local;
        int tropasInt = (int)tropas;
        TropasDisponibles.Value += tropasInt;

        // Preparar RemovedCardsInfo
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

        Debug.Log($"Server: Canje válido para player {OwnerClientId}. +{tropasInt} tropas. n_change global ahora = {Mazo.CartasIntercambiadas}");
    }

    public void AgregarCartaManoLogica_Server(Carta carta)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (carta == null) return;

        Mano.AgregarCartaMano(carta);

        int territoryIdx = (carta.territorio != null) ? carta.territorio.Idx : -1;
        int tipoCode = (int)carta.tipo;

        var clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { this.OwnerClientId } }
        };

        ShowObtainedCardClientRpc(territoryIdx, tipoCode, clientRpcParams);
    }

    [ClientRpc]
    private void ShowObtainedCardClientRpc(int territoryIdx, int tipoCode, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"📬 Recibiste carta: tipo={tipoCode}, territoryIdx={territoryIdx}");

        if (IsOwner && localManoUI != null)
        {
            localManoUI.MostrarCartaObtenidaVisual(territoryIdx, (CardType)tipoCode);
        }
        else
        {
            var manoUI = FindFirstObjectByType<ManoJugadorUI>();
            if (manoUI != null) manoUI.MostrarCartaObtenidaVisual(territoryIdx, (CardType)tipoCode);
            else Debug.LogWarning("ShowObtainedCardClientRpc: no se encontró ManoJugadorUI local para mostrar la carta.");
        }
    }

    [ClientRpc]
    private void NotifyCanjeResultClientRpc(bool exito, int tropas, RemovedCardsInfo removed, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner && localManoUI != null)
        {
            localManoUI.HandleCanjeResult(exito, tropas, removed);
        }
        else
        {
            var manoUI = FindFirstObjectByType<ManoJugadorUI>();
            if (manoUI != null) manoUI.HandleCanjeResult(exito, tropas, removed);
            else Debug.LogWarning("NotifyCanjeResultClientRpc: no se encontró ManoJugadorUI en cliente.");
        }
    }

    public bool ComprobarTerritorio(Territory terr) => Territorios.Contains(terr);

    public void AgregarTerritorio(Territory terr)
    {
        if (!Territorios.Contains(terr))
        {
            Territorios.Add(terr);
            terr.Owner = this;
            terr.PlayerColor = ColorJugador.Value;
            terr.UpdateColor();
        }
    }

    public void EliminarTerritorio(Territory terr)
    {
        if (Territorios.Contains(terr))
        {
            Territorios.Remove(terr);
            terr.Owner = null;
            terr.PlayerColor = terr.NeutralColor;
            terr.UpdateColor();
        }
    }
}



