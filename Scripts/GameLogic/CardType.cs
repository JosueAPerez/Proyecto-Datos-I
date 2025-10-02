using Unity.Netcode;
using Unity.Collections;
//permite facvorecer la serializacion de las cartas

public struct CardSelection : INetworkSerializable
{
    public int idx0, idx1, idx2; //los indices del enum CardType
    public int tipo0, tipo1, tipo2; //Los tipos de cartas que hay

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        //serializa todos los valores de los indices y tipos, para una mayor de facilidad de procesamiento
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
    //metodos para obtener el indice y el tipo de cartas removidas
    public int GetIdxAt(int i) => i == 0 ? idx0 : i == 1 ? idx1 : i == 2 ? idx2 : -1;
    public CardType GetTipoAt(int i) => (CardType)(i == 0 ? tipo0 : i == 1 ? tipo1 : tipo2);
}

public struct CardData : INetworkSerializable
{
    public int territoryIdx;
    public int tipo;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref territoryIdx);
        serializer.SerializeValue(ref tipo);
    }
}
