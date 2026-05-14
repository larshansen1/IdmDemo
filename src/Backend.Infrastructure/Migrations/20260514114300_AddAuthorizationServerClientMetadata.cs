using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public partial class AddAuthorizationServerClientMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedRoleValues",
                table: "MachineClients",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AssignedScopeValues",
                table: "MachineClients",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CertificateExpiresAt",
                table: "MachineClients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateSubject",
                table: "MachineClients",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateThumbprintSha256",
                table: "MachineClients",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedRoleValues",
                table: "MachineClients");

            migrationBuilder.DropColumn(
                name: "AssignedScopeValues",
                table: "MachineClients");

            migrationBuilder.DropColumn(
                name: "CertificateExpiresAt",
                table: "MachineClients");

            migrationBuilder.DropColumn(
                name: "CertificateSubject",
                table: "MachineClients");

            migrationBuilder.DropColumn(
                name: "CertificateThumbprintSha256",
                table: "MachineClients");
        }
    }
}
