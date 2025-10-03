using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class ManoJugadorUI : MonoBehaviour
{
    [Header("Prefabs & UI (asignar)")]
    public GameObject cartaUIPrefab;
    public Transform contenedorCartas;
    public Transform contenedorPopup;
    public Button btnAbrirMano;
    public Button btnCanjear;
    public Button btnCerrarMano;
    public Text txtMensaje;

    public ManoJugador manoLogica = new ManoJugador();
    private MyArray<CartasUI> cartasVisuales;
    private bool manoAbierta = false;
    private NetworkPlayer ownerPlayer;
    private int maxSeleccion = 3;
    private int selectedCount = 0;

    public void Initialize(NetworkPlayer owner)
    {
        ownerPlayer = owner;
        cartasVisuales = new MyArray<CartasUI>(6);
        if (btnAbrirMano != null) { btnAbrirMano.onClick.RemoveAllListeners(); btnAbrirMano.onClick.AddListener(ToggleMano); }
        if (btnCanjear != null) { btnCanjear.onClick.RemoveAllListeners(); btnCanjear.onClick.AddListener(OnClickCanjear); }
        if (btnCerrarMano != null) { btnCerrarMano.onClick.RemoveAllListeners(); btnCerrarMano.onClick.AddListener(CerrarMano); }
        gameObject.SetActive(false);
    }

    private void Start()
    {
        if (ownerPlayer == null)
        {
            var np = FindFirstObjectByType<NetworkPlayer>();
            if (np != null && np.IsOwner) Initialize(np);
        }
    }

    public void ToggleMano()
    {
        manoAbierta = !manoAbierta;
        gameObject.SetActive(manoAbierta);
        if (manoAbierta) RefrescarUI();
    }

    public void CerrarMano() { manoAbierta = false; gameObject.SetActive(false); }

    public void MostrarCartaObtenidaVisual(int territoryIdx, CardType tipo)
    {
        Territory terr = null;
        if (BoardManager.Instance != null)
        {
            foreach (var t in BoardManager.Instance.territories) if (t != null && t.Idx == territoryIdx) { terr = t; break; }
        }
        Carta nueva = new Carta(tipo, terr);
        manoLogica.AgregarCartaMano(nueva);

        Transform parent = contenedorPopup != null ? contenedorPopup : contenedorCartas;
        GameObject go = Instantiate(cartaUIPrefab, parent);
        CartasUI cui = go.GetComponent<CartasUI>();
        Sprite s = SpriteManager.GetSpriteForCarta(nueva);
        cui.Configurar(nueva, s);
        cui.OnClicked += OnCartaClicked;
        StartCoroutine(EliminarPopupDespues(go, 3f));
        MostrarMensaje($"Has recibido una carta: {tipo}");
    }

    private IEnumerator EliminarPopupDespues(GameObject go, float tiempo)
    {
        yield return new WaitForSeconds(tiempo);
        if (go != null) Destroy(go);
        if (manoAbierta) RefrescarUI();
    }

    public void RefrescarUI()
    {
        if (cartasVisuales != null)
            foreach (var c in cartasVisuales) if (c != null) Destroy(c.gameObject);

        cartasVisuales = new MyArray<CartasUI>(6);
        selectedCount = 0;

        for (int i = 0; i < manoLogica.mano.Count; i++)
        {
            Carta logica = manoLogica.mano[i];
            GameObject go = Instantiate(cartaUIPrefab, contenedorCartas);
            CartasUI cui = go.GetComponent<CartasUI>();
            Sprite s = SpriteManager.GetSpriteForCarta(logica);
            cui.Configurar(logica, s);
            cui.OnClicked += OnCartaClicked;
            cartasVisuales.Add(cui);
        }
    }

    private void OnCartaClicked(CartasUI clicked)
    {
        if (clicked == null) return;
        if (!clicked.EstaSeleccionada() && selectedCount >= maxSeleccion) { MostrarMensaje($"Solo puedes seleccionar {maxSeleccion} cartas."); return; }
        clicked.ToggleSeleccion();
        selectedCount = 0;
        for (int i = 0; i < cartasVisuales.Count; i++)
        {
            var cu = cartasVisuales[i];
            if (cu != null && cu.EstaSeleccionada()) selectedCount++;
        }
    }

    public void MostrarMensaje(string texto)
    {
        if (txtMensaje != null) txtMensaje.text = texto; else Debug.Log(texto);
    }

    private void OnClickCanjear()
    {
        MyArray<Carta> seleccionLogica = new MyArray<Carta>(3);
        for (int i = 0; i < cartasVisuales.Count; i++)
        {
            var cu = cartasVisuales[i];
            if (cu != null && cu.EstaSeleccionada()) seleccionLogica.Add(cu.datosLogicos);
        }
        if (seleccionLogica.Count != 3) { MostrarMensaje("Selecciona exactamente 3 cartas para canjear."); return; }

        CardSelection sel = new CardSelection();
        sel.idx0 = (seleccionLogica[0].territorio != null) ? seleccionLogica[0].territorio.Idx : -1;
        sel.idx1 = (seleccionLogica[1].territorio != null) ? seleccionLogica[1].territorio.Idx : -1;
        sel.idx2 = (seleccionLogica[2].territorio != null) ? seleccionLogica[2].territorio.Idx : -1;
        sel.tipo0 = (int)seleccionLogica[0].tipo;
        sel.tipo1 = (int)seleccionLogica[1].tipo;
        sel.tipo2 = (int)seleccionLogica[2].tipo;

        if (ownerPlayer != null && ownerPlayer.IsOwner)
        {
            ownerPlayer.RequestCanjearServerRpc(sel);
            MostrarMensaje("Solicitud de canje enviada al servidor...");
        }
        else MostrarMensaje("Player network no disponible para solicitar canje.");
    }

    // Recibir un snapshot de mano desde server (si implementas RequestHand)
    public void RecibirManoDesdeServer(CardData[] hand)
    {
        manoLogica = new ManoJugador();
        if (hand == null) return;
        for (int i = 0; i < hand.Length; i++)
        {
            Territory terr = null;
            if (BoardManager.Instance != null) terr = BoardManager.Instance.GetTerritoryByIdx(hand[i].territoryIdx);
            Carta c = new Carta((CardType)hand[i].tipo, terr);
            manoLogica.AgregarCartaMano(c);
        }
        RefrescarUI();
    }

    // Manejar resultado del canje (server notifica)
    public void HandleCanjeResult(bool exito, int tropas, RemovedCardsInfo removed)
    {
        if (!exito) { MostrarMensaje("Canje inválido."); return; }
        if (removed.count <= 0) { MostrarMensaje($"Canje válido: +{tropas} tropas (sin info de cartas)."); RefrescarUI(); return; }

        for (int k = 0; k < removed.count; k++)
        {
            int ridx = removed.GetIdxAt(k);
            CardType rtipo = removed.GetTipoAt(k);
            Carta encontrada = null;
            foreach (var c in manoLogica.mano) if (c != null && ((c.territorio != null ? c.territorio.Idx : -1) == ridx) && c.tipo == rtipo) { encontrada = c; break; }
            if (encontrada != null) manoLogica.mano.Remove(encontrada);

            CartasUI visualARemover = null;
            for (int i = 0; i < cartasVisuales.Count; i++)
            {
                var cu = cartasVisuales[i];
                if (cu != null && cu.datosLogicos != null)
                {
                    int cIdx = (cu.datosLogicos.territorio != null) ? cu.datosLogicos.territorio.Idx : -1;
                    if (cIdx == ridx && cu.datosLogicos.tipo == rtipo) { visualARemover = cu; break; }
                }
            }
            if (visualARemover != null) { cartasVisuales.Remove(visualARemover); if (visualARemover.gameObject != null) Destroy(visualARemover.gameObject); }
        }

        MostrarMensaje($"Canje válido: +{tropas} tropas obtenidas.");
        RefrescarUI();
    }
}

