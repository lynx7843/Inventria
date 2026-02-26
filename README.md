# 📦 Inventria - Inventory Management System

A desktop-based Inventory Management System developed using C# Windows Forms in Microsoft Visual Studio, designed to help manage products, stock levels, and inventory operations efficiently.  
The system is connected to a local MongoDB database, ensuring fast and flexible data storage without the complexity of traditional relational databases.

## 🚀 Features
* **Add** new products to inventory
* **Search** and view product details
* **Update** product information
* **Delete** products
* **Track** stock levels
* **Categorize** inventory items
* **Fast** local database access using MongoDB
* **Simple** and user-friendly Windows Forms UI

## 🛠 Tech Stack

| Technology | Description |
| :--- | :--- |
| **C#** | Core programming language |
| **Windows Forms** | Desktop UI Framework |
| **Visual Studio** | Development Environment |
| **MongoDB** | NoSQL Local Database |
| **MongoDB.Driver** | C# Driver for MongoDB |

## ⚙️ Requirements
Before running the project, ensure you have the following installed on your system:
* ✔️ Visual Studio
* ✔️ .NET Framework
* ✔️ MongoDB Local Server

## ▶️ Getting Started

### 1. MongoDB Setup
Once MongoDB is installed, open your terminal or command prompt and start the server by running:
```bash
mongod
Default connection: mongodb://localhost:27017

Database Name: InventoryDB

2. Running the Application
Clone the repository to your local machine:

Bash
git clone [https://github.com/lynx7843/Inventria.git](https://github.com/lynx7843/Inventria.git)
Open the InventoryManagementSystem.sln solution file in Visual Studio.

Restore the necessary NuGet packages (ensure MongoDB.Driver is installed).

Press Start (F5) to build and run the project.

🔐 Future Improvements
[ ] User authentication

[ ] Sales tracking

[ ] Supplier management

[ ] Report generation

[ ] Barcode scanning

[ ] Cloud database support