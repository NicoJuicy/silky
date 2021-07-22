﻿// <auto-generated />
using System;
using Silky.Order.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Silky.Order.EntityFrameworkCore.Migrations
{
    [DbContext(typeof(OrderDbContext))]
    [Migration("20210404040215_InitCreate")]
    partial class InitCreate
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("Relational:MaxIdentifierLength", 64)
                .HasAnnotation("ProductVersion", "5.0.4");

            modelBuilder.Entity("Silky.Order.Domain.Orders.Order", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    b.Property<long>("AccountId")
                        .HasColumnType("bigint");

                    b.Property<decimal>("Amount")
                        .HasColumnType("decimal(10,2)");

                    b.Property<long?>("CreateBy")
                        .HasColumnType("bigint");

                    b.Property<DateTime>("CreateTime")
                        .HasColumnType("datetime(6)");

                    b.Property<long>("ProductId")
                        .HasColumnType("bigint");

                    b.Property<int>("Quantity")
                        .HasColumnType("int");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<long?>("UpdateBy")
                        .HasColumnType("bigint");

                    b.Property<DateTime>("UpdateTime")
                        .HasColumnType("datetime(6)");

                    b.HasKey("Id");

                    b.ToTable("Orders");
                });
#pragma warning restore 612, 618
        }
    }
}