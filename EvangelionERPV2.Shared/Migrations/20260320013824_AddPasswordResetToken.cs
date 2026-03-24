using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[PasswordResetToken]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [PasswordResetToken] (
                        [Id] uniqueidentifier NOT NULL,
                        [UserId] uniqueidentifier NOT NULL,
                        [TokenHash] nvarchar(450) NOT NULL,
                        [ExpiresAt] datetime2 NOT NULL,
                        [UsedAt] datetime2 NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [UpdatedAt] datetime2 NULL,
                        [IsActive] bit NULL,
                        CONSTRAINT [PK_PasswordResetToken] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_PasswordResetToken_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([Id]) ON DELETE CASCADE
                    );
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PasswordResetToken_Id_CreatedAt_UpdatedAt' AND object_id = OBJECT_ID(N'[PasswordResetToken]'))
                    CREATE INDEX [IX_PasswordResetToken_Id_CreatedAt_UpdatedAt] ON [PasswordResetToken] ([Id], [CreatedAt], [UpdatedAt]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PasswordResetToken_Id_CreatedAt_UpdatedAt_IsActive' AND object_id = OBJECT_ID(N'[PasswordResetToken]'))
                    CREATE INDEX [IX_PasswordResetToken_Id_CreatedAt_UpdatedAt_IsActive] ON [PasswordResetToken] ([Id], [CreatedAt], [UpdatedAt], [IsActive]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PasswordResetToken_TokenHash' AND object_id = OBJECT_ID(N'[PasswordResetToken]'))
                    CREATE UNIQUE INDEX [IX_PasswordResetToken_TokenHash] ON [PasswordResetToken] ([TokenHash]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PasswordResetToken_UserId_IsActive_ExpiresAt' AND object_id = OBJECT_ID(N'[PasswordResetToken]'))
                    CREATE INDEX [IX_PasswordResetToken_UserId_IsActive_ExpiresAt] ON [PasswordResetToken] ([UserId], [IsActive], [ExpiresAt]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[PasswordResetToken]', N'U') IS NOT NULL
                    DROP TABLE [PasswordResetToken];
                """);
        }
    }
}
