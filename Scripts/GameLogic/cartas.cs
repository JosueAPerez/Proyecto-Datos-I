using UnityEngine;
using System;

[System.Serializable]
public class Carta
{
    public CardType tipo { get; private set; }
    public Territory territorio { get; private set; }
    public int id { get; private set; }
    public Carta(CardType type, Territory territory)
    {
        this.territorio = territory;
        this.tipo = type;
        this.id = (territorio!= null) ? territorio.Idx : -1;
    }
}

public class Mazo
{
    public MyArray<Territory> Territorios = new MyArray<Territory>(42);
    public static MyArray<Carta> cartas { get; private set; } = new MyArray<Carta>(42);
    public MyArray<CardType> tipos = new MyArray<CardType>(3);

    public static int CartasIntercambiadas;
    public Mazo(MyArray<Territory> territorios)
    {
        tipos.Add(CardType.Infanteria);
        tipos.Add(CardType.Caballeria);
        tipos.Add(CardType.Artilleria);
        Territorios = territorios;
        CartasIntercambiadas = 0;
    }
    public void LlenarMazo()
    {
        for (int i = 0; i < cartas.Capacity; i++)
        {
            CardType tipo = tipos[i % 3];
            cartas.Add(new Carta(tipo, Territorios[i]));
        }

        this.Shuffle(cartas);
    }
    public Carta RobarCarta()
    {
        if (cartas.Count == 0) return null;

        int idx_carta = UnityEngine.Random.Range(0, cartas.Count);

        int idx_ocupado = cartas.Count - 1; 

        Carta cart = cartas[idx_carta];

        cartas[idx_carta] = cartas[idx_ocupado];

        cartas.RemoveIdx(idx_ocupado);

        return cart;
    }

    public void AgregarCarta(MyArray<Carta>carts) //para cuando las cartas se utilicen vuelvan al mazo
    {

        if (carts.Capacity != 3) return;

        for (int i = 0; i < carts.Capacity; i++)
        {
            cartas.Add(carts[i]);
        }

        CartasIntercambiadas += carts.Count;
        if (CartasIntercambiadas >= 21)
        {
            this.Shuffle(cartas);
            CartasIntercambiadas = 0;
        }
    }
    public void Shuffle<T>(MyArray<T> array) //lo hizo gpt
    {
        int n = array.Count;

        for (int i = n - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1); // índice aleatorio entre 0 e i
            // Intercambiar array[i] con array[j]
            T temp = array[i];
            array[i] = array[j];
            array[j] = temp;
        }
    }
}

public class ManoJugador
{
    public MyArray<Carta> mano { get; private set; } = new MyArray<Carta>(6);

    public void AgregarCartaMano(Carta cart)
    {
        if (mano.Count < mano.Capacity && BoardManager.Instance != null)
        {
            mano.Add(cart);
        }

        else { Debug.Log("La mano esta llena"); return; }
    }

    public bool VerificarSeleccion(MyArray<Carta>selec)
    {
        if (selec == null || selec.Count != 3) return false;

        CardType car1 = selec[0].tipo;
        CardType car2 = selec[1].tipo;
        CardType car3 = selec[2].tipo;

        bool ver1 = (car1 == car2) && (car2 == car3);
        bool ver2 = (car1 != car2) && (car2 != car3) && (car1 != car3);

        return ver1 || ver2;
    }

    public ulong Canjear(MyArray<Carta> selec, ref int n_change)
    {
        if (!VerificarSeleccion(selec))
        {
            Debug.Log("No se puede canjear");
            return 0;
        }

        for (int i = 0; i < selec.Count; i++)
        {
            mano.Remove(selec[i]);
        }

        n_change++;

        return Fibo.Calc(n_change);
    }
    public Carta ObtenerCartaiD(int id)
    {
        foreach (var carta in mano)
        {
            if (carta != null && carta.id == id)
            {
                return carta;
            }
        }
        return null;
    }
}

public static class Fibo
{
    private static ulong a = 2;
    private static ulong b = 3;

    private static int actual = 2;

    private const ulong Max_Tropas = 144UL;

    public static ulong Calc(int n)

    {
        if (n == 1) return 2;
        if (n == 2) return 3;

        if (n != actual + 1) throw new InvalidOperationException("Los canjes deben ser consecutivos");

        ulong res;

        if (ulong.MaxValue - b < a) res = Max_Tropas;
        else res = a + b;

        ulong final = (res > Max_Tropas) ? Max_Tropas : res;

        a = b;

        b = final;

        actual += 1;

        return final;

    }
    public static void Reset()
    {
        a = 2;
        b = 3;
        actual = 2;
    }
}