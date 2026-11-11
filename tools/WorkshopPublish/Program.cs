if (args.Length != 7 || args[0] is not ("publish" or "prepare" or "check"))
{
    Console.Error.WriteLine("Usage: WorkshopPublish <publish|prepare|check> <settings.json> <readme> <changelog> <content-folder> <preview-image> <output-folder>");
    return 1;
}

try
{
    WorkshopPublisher.Run(args[0], args[1], args[2], args[3], args[4], args[5], args[6], () => new SteamWorkshop());
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
