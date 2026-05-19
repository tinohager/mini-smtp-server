public class Program
{
    public static void Main(string[] args)
    {
        string? version = null;

        if (args.Length > 0)
        {
            version = args[0];
        }
        else
        {
            Console.WriteLine("========================================");
            Console.WriteLine(" MiniSmtpServer - Bootstrapper");
            Console.WriteLine("========================================");
            Console.Write("Which version would you like to start? (1-12) [Default: 12]: ");

            var input = Console.ReadLine();
            version = string.IsNullOrWhiteSpace(input) ? "12" : input.Trim();
        }

        Console.WriteLine($"\n[Boot] Starting SMTP Server Version: {version}...");
        Console.WriteLine("----------------------------------------");

        switch (version)
        {
            case "1": SmtpServer1.Start(); break;
            case "2": SmtpServer2.Start(); break;
            case "3": SmtpServer3.Start(); break;
            case "4": SmtpServer4.Start(); break;
            case "5": SmtpServer5.Start(); break;
            case "6": SmtpServer6.Start(); break;
            case "7": SmtpServer7.Start(); break;
            case "8": SmtpServer8.Start(); break;
            case "9": SmtpServer9.Start(); break;
            case "10": SmtpServer10.Start(); break;
            case "11": SmtpServer11.Start(); break;
            case "12": SmtpServer12.Start(); break;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fehler: Version '{version}' ist unbekannt.");
                Console.ResetColor();
                Console.WriteLine("Starte stattdessen Standard-Version 12.");
                SmtpServer12.Start();
                break;
        }
    }
}