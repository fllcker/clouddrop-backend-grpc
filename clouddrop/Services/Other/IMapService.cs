namespace clouddrop.Services.Other;

public interface IMapService
{
    public T Map<F, T>(F source); // F - from, T - to
}