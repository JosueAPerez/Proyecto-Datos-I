using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class BoardManager : MonoBehaviour
{
    public static BoardManager Instance;
    public Territory[] Territories;
    private Dictionary<int, Territory> territoryById = new Dictionary<int, Territory>();
    private List<NetworkPlayer> jugadoresConectados = new List<NetworkPlayer>();
    public Mazo mazo; //logica del mazo
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        Territories = FindObjectsByType<Territory>(FindObjectsSortMode.None);
        System.Array.Sort(Territories, (a, b) => a.Idx.CompareTo(b.Idx));

        foreach (var t in Territories)
        {
            if (territoryById.ContainsKey(t.Idx))
                Debug.LogWarning($"⚠️ Duplicate Idx: {t.Idx} en {t.name}");
            else
                territoryById[t.Idx] = t;
        }

        LoadAdjacency();
        
         // Inicializar el mazo lógico usando MyArray<Territory>
        MyArray<Territory> listaTerr = new MyArray<Territory>(Territories.Length);
        for (int i = 0; i < Territories.Length; i++)
            listaTerr.Add(Territories[i]);

        mazo = new Mazo(listaTerr);
        mazo.LlenarMazo();

        Debug.Log($"✅ BoardManager initialized with {territoryById.Count} territories. Mazo llenado con {Mazo.cartas.Count} cartas.");

        // Hook de Netcode para escena cargada
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
        }
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        Debug.Log($"🔄 Escena cargada con Netcode: {sceneName}");

        // Si ya hay jugadores spawneados, registrarlos
        foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            RegistrarJugador(player);
        }
    }

    public void RegistrarJugador(NetworkPlayer p)
    {
        if (p == null) return;

        if (!jugadoresConectados.Contains(p))
        {
            jugadoresConectados.Add(p);

            if (!string.IsNullOrEmpty(p.Alias.Value.ToString()))
            {
                Debug.Log($"🧑‍🎮 Se unió: {p.Alias.Value.ToString()} (ClientId={p.OwnerClientId})");
            }
            else
            {
                Debug.Log($"🧑‍🎮 Se unió jugador sin alias (ClientId={p.OwnerClientId})");
            }
        }

        // Solo el servidor reparte
        if (NetworkManager.Singleton.IsServer && jugadoresConectados.Count >= 2)
        {
            RepartirContinentes();
        }
    }

    private void LoadAdjacency()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>("adjacency");
        if (jsonFile == null)
        {
            Debug.LogError("❌ adjacency.json no encontrado.");
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

    private void RepartirContinentes()
    {
        int totalPlayers = jugadoresConectados.Count;
        if (totalPlayers == 0) return;

        int territoriosPorJugador = Territories.Length / totalPlayers;
        int currentIdx = 0;

        foreach (var player in jugadoresConectados)
        {
            for (int i = 0; i < territoriosPorJugador && currentIdx < Territories.Length; i++, currentIdx++)
            {
                var terr = Territories[currentIdx];
                terr.Owner = player;
                terr.PlayerColor = player.ColorJugador.Value;
                terr.Soldiers = 1;
                terr.UpdateColor();

                player.AgregarTerritorio(terr);
            }

            Debug.Log($"✅ {player.Alias.Value.ToString()} recibió {territoriosPorJugador} territorios.");
        }
    }

    public void DarCartaAJugador(NetworkPlayer jugador)
    {
        if (jugador == null) return;

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("DarCartaAJugador: solo el servidor debe ejecutar este método.");
            return;
        }

        if (mazo == null)
        {
            Debug.LogError("DarCartaAJugador: mazo es null.");
            return;
        }

        Carta cartaRobada = mazo.RobarCarta();
        if (cartaRobada == null)
        {
            Debug.Log("Mazo vacío: no se puede dar carta.");
            return;
        }

        // Actualizar la mano server-side y notificar al cliente propietario
        jugador.AgregarCartaManoLogica_Server(cartaRobada);

        Debug.Log($"Servidor: dio carta '{cartaRobada.tipo}' (territorio idx={cartaRobada.territorio?.Idx}) al jugador {jugador.Alias.Value.ToString()}");
    }

    
    // Buscar Territory por idx (útil en clientes)
    public Territory GetTerritoryByIdx(int idx)
    {
        territoryById.TryGetValue(idx, out Territory t);
        return t;
    }

    [System.Serializable]
    private class SerializationWrapper
    {
        public TerritoryEntry[] entries;
    }

    [System.Serializable]
    private class TerritoryEntry
    {
        public int id;
        public int[] neighbors;
    }

    public void OnTerritoryClicked(Territory t)
    {
        Debug.Log($"[Click] {t.TerritoryName} (Idx={t.Idx}, Owner={t.Owner}, Soldiers={t.Soldiers})");
    }

}





