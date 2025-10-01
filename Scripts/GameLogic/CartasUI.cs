using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Componente UI para una carta (Image en Canvas).
/// Soporta IPointerClickHandler y notifica mediante Action cuando es clickeada.
/// </summary>
public class CartasUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI refs")]
    public Image cartaImage;        // arrastra la Image del prefab
    public GameObject highlight;    // borde/efecto para selección, child enabled/disabled
    public Text txtTipo;            // (opcional) texto donde se muestra "Infantería", etc.

    [HideInInspector] public Carta datosLogicos; // referencia a la carta lógica
    private bool seleccionada = false;

    public Action<CartasUI> OnClicked;

    public void Configurar(Carta carta, Sprite spriteCarta)
    {
        datosLogicos = carta;
        if (cartaImage == null) cartaImage = GetComponentInChildren<Image>();
        MostrarSprite(spriteCarta);
        ActualizarTipoTexto();
        Deseleccionar();
        gameObject.SetActive(true);
    }

    public void MostrarSprite(Sprite s)
    {
        if (cartaImage == null) return;
        cartaImage.sprite = s;
        cartaImage.enabled = true;
    }

    public void Ocultar()
    {
        if (cartaImage == null) return;
        cartaImage.enabled = false;
        datosLogicos = null;
        Deseleccionar();
    }

    public void OnClickCarta()
    {
        ToggleSeleccion();
        OnClicked?.Invoke(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnClickCarta();
    }

    public void ToggleSeleccion()
    {
        seleccionada = !seleccionada;
        if (highlight != null) highlight.SetActive(seleccionada);
    }

    public void Seleccionar()
    {
        seleccionada = true;
        if (highlight != null) highlight.SetActive(true);
    }

    public void Deseleccionar()
    {
        seleccionada = false;
        if (highlight != null) highlight.SetActive(false);
    }

    public bool EstaSeleccionada() => seleccionada;

    private void ActualizarTipoTexto()
    {
        if (txtTipo == null) return;
        if (datosLogicos == null)
        {
            txtTipo.text = "";
            return;
        }

        txtTipo.text = TipoToLabel(datosLogicos.tipo);
    }

    private string TipoToLabel(CardType t)
    {
        switch (t)
        {
            case CardType.Infanteria: return "Infantería";
            case CardType.Caballeria: return "Caballería";
            case CardType.Artilleria: return "Artillería";
            default: return t.ToString();
        }
    }

    public void ActualizarSelectionVisual(bool state)
    {
        seleccionada = state;
        if (highlight != null) highlight.SetActive(state);
    }
}
