using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField inputNombre;
    public TMP_InputField inputIP;
    public Button botonHost;
    public Button botonClient;

    [Header("Config")]
    [SerializeField] private string gameSceneName = "GameScene";
    [SerializeField] private int port = 7777;

    void Start()
    {
        botonHost.onClick.AddListener(OnHostClick);
        botonClient.onClick.AddListener(OnClientClick);
    }

    private string GetLocalIPAddress()
    {
        string localIP = "127.0.0.1"; // fallback
        try
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                localIP = endPoint.Address.ToString();
            }
        }
        catch
        {
            Debug.LogWarning("No se pudo detectar la IP local, usando 127.0.0.1");
        }
        return localIP;
    }
    public void OnHostClick()
    {
        GameSettings.NombreJugador = string.IsNullOrWhiteSpace(inputNombre.text) ? "JugadorPorDefecto" : inputNombre.text;
        string ip = GetLocalIPAddress();
        ConfigurarTransporte(ip);
        DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log($"Host iniciado en {ip}:{port}");
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
    }

    public void OnClientClick()
    {
        GameSettings.NombreJugador = string.IsNullOrWhiteSpace(inputNombre.text) ? "JugadorPorDefecto" : inputNombre.text;
        ConfigurarTransporte(inputIP.text);

        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log($"Cliente intentando conectar a {inputIP.text}:{port}");
        }
    }

    private void ConfigurarTransporte(string ipAddress)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.ConnectionData.Address = ipAddress;
        transport.ConnectionData.Port = (ushort)port;
    }

}

