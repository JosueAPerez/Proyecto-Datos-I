using Unity.Netcode;
using Unity.Collections;

public enum CardType
{
    Infanteria = 0,
    Caballeria = 1,
    Artilleria = 2
}

public struct CardSelection : INetworkSerializable
{
    public int idx0, idx1, idx2;
    public int tipo0, tipo1, tipo2; // serializamos enums como int

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref idx0);
        serializer.SerializeValue(ref idx1);
        serializer.SerializeValue(ref idx2);
        serializer.SerializeValue(ref tipo0);
        serializer.SerializeValue(ref tipo1);
        serializer.SerializeValue(ref tipo2);
    }
}

public struct RemovedCardsInfo : INetworkSerializable
{
    public int count;
    public int idx0, idx1, idx2;
    public int tipo0, tipo1, tipo2;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref count);
        serializer.SerializeValue(ref idx0);
        serializer.SerializeValue(ref idx1);
        serializer.SerializeValue(ref idx2);
        serializer.SerializeValue(ref tipo0);
        serializer.SerializeValue(ref tipo1);
        serializer.SerializeValue(ref tipo2);
    }

    public int GetIdxAt(int i)
    {
        switch (i)
        {
            case 0: return idx0;
            case 1: return idx1;
            case 2: return idx2;
            default: return -1;
        }
    }

    public CardType GetTipoAt(int i)
    {
        switch (i)
        {
            case 0: return (CardType)tipo0;
            case 1: return (CardType)tipo1;
            case 2: return (CardType)tipo2;
            default: return CardType.Infanteria;
        }
    }
}