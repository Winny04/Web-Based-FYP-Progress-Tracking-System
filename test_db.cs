using System;
using System.Data.SqlClient;

class Program
{
    static void Main()
    {
        string connStr = "server=Boon\\SQLEXPRESS;Database=FYPTracker;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
        using (SqlConnection conn = new SqlConnection(connStr))
        {
            conn.Open();
            SqlCommand cmd = new SqlCommand("SELECT Id, Title, Status, DefenseDate, DefenseTime FROM FYPMilestone WHERE Title LIKE '%IAF%' OR Title LIKE '%Initial Project Allocation%'", conn);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                Console.WriteLine("Id | Title | Status | DefenseDate | DefenseTime");
                while (reader.Read())
                {
                    Console.WriteLine($"{reader["Id"]} | {reader["Title"]} | {reader["Status"]} | {reader["DefenseDate"]} | {reader["DefenseTime"]}");
                }
            }
        }
    }
}
