namespace AmazonScraper.Cli.Library;

public class SavedLibraryException(string message, Exception? inner = null)
    : Exception(message, inner);
