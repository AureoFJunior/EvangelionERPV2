using Microsoft.EntityFrameworkCore;
using System;

namespace EvangelionERPV2.Domain.Models
{
    [Index(nameof(UserName), nameof(Password))]
    public class User : BaseEntity
    {
        public User() { }

        public User(string firstName, string lastName, string userName, string password, string email, DateTime birthDate, string profilePicture = "", short? isLogged = 0, short actualTheme = 0)
        {
            FirstName = firstName;
            LastName = lastName;
            UserName = userName;
            Password = password;
            Email = email;
            BirthDate = birthDate;
            IsLogged = isLogged;
            ProfilePicture = profilePicture;
            ActualTheme = actualTheme;
        }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public DateTime BirthDate { get; set; }
        public short? IsLogged { get; set; }
        public short ActualTheme { get; set; }
        public string ProfilePicture { get; set; }
    }
}