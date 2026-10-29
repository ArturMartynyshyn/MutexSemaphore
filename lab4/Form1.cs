using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace lab4
{
    public partial class Form1 : Form
    {
        private ListBox listBoxLogs;
        private Button btnStart;

        private Semaphore printersSemaphore = null!;
        private Mutex paperMutex = null!;
        private Barrier shiftBarrier = null!;
        private readonly List<PrintOrder> pendingOrders = new();
        private List<PrintOrder> orders = new();

        private int paperCount = 3;
        private int totalOrders = 10;
        private int nextDeliveryIndex;
        private int nextPrintIndex;
        private int collectedOrders;

        private const int WorkerCount = 2;
        private const int PrinterWorkerCount = 4;
        private const int AvailablePrinters = 2;

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            listBoxLogs = new ListBox
            {
                Location = new Point(12, 50),
                Size = new Size(760, 380),
                Font = new Font("Consolas", 10F)
            };

            btnStart = new Button
            {
                Location = new Point(12, 12),
                Size = new Size(150, 30),
                Text = "Почати зміну"
            };
            btnStart.Click += BtnStart_Click;

            Controls.Add(listBoxLogs);
            Controls.Add(btnStart);
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            btnStart.Enabled = false;
            listBoxLogs.Items.Clear();
            paperCount = 3;
            nextDeliveryIndex = 0;
            nextPrintIndex = 0;
            collectedOrders = 0;
            pendingOrders.Clear();
            orders = new List<PrintOrder>();

            for (int i = 1; i <= totalOrders; i++)
            {
                orders.Add(new PrintOrder(i, i % 4 == 0));
            }

            printersSemaphore = new Semaphore(AvailablePrinters, AvailablePrinters);
            paperMutex = new Mutex();
            shiftBarrier = new Barrier(WorkerCount + PrinterWorkerCount, _ =>
                LogMessage("=== УСІ ЗАМОВЛЕННЯ ДОСТАВЛЕНО. ЗМІНА РОЗПОЧАЛАСЯ ==="));

            foreach (PrintOrder order in orders)
            {
                Thread customer = new Thread(CollectOrder)
                {
                    Name = $"Клієнт для замовлення {order.Id}"
                };
                customer.Start(order);
            }

            for (int i = 1; i <= WorkerCount; i++)
            {
                Thread worker = new Thread(DeliverOrders)
                {
                    Name = $"Працівник {i}"
                };
                worker.Start(i);
            }

            for (int i = 1; i <= PrinterWorkerCount; i++)
            {
                Thread printer = new Thread(PrintOrders)
                {
                    Name = $"Друкарська машина {i}",
                    Priority = ThreadPriority.Normal
                };
                printer.Start();
            }
        }

        private void DeliverOrders(object? workerIndexObj)
        {
            int workerIndex = (int)workerIndexObj!;

            while (true)
            {
                int orderIndex = Interlocked.Increment(ref nextDeliveryIndex) - 1;
                if (orderIndex >= orders.Count)
                {
                    break;
                }

                PrintOrder order = orders[orderIndex];
                string label = order.IsVip ? $"[VIP] Замовлення {order.Id}" : $"Замовлення {order.Id}";
                LogMessage($"{Thread.CurrentThread.Name} приніс {label}.");
                Thread.Sleep(100 + workerIndex * 50);

                paperMutex.WaitOne();
                try
                {
                    pendingOrders.Add(order);
                }
                finally
                {
                    paperMutex.ReleaseMutex();
                }

                LogMessage($"{label} доставлено до друкарні.");
            }

            shiftBarrier.SignalAndWait();
        }

        private void PrintOrders()
        {
            shiftBarrier.SignalAndWait();

            while (true)
            {
                int orderNumber = Interlocked.Increment(ref nextPrintIndex);
                if (orderNumber > orders.Count)
                {
                    break;
                }

                printersSemaphore.WaitOne();
                PrintOrder order;
                int currentPaper;
                try
                {
                    paperMutex.WaitOne();
                    try
                    {
                        int orderIndex = pendingOrders.FindIndex(candidate => candidate.IsVip);
                        if (orderIndex < 0)
                        {
                            orderIndex = 0;
                        }

                        order = pendingOrders[orderIndex];
                        pendingOrders.RemoveAt(orderIndex);

                        if (paperCount <= 0)
                        {
                            LogMessage($"Папір закінчився для замовлення {order.Id}. Завантаження нового лотка...");
                            Thread.Sleep(1000);
                            paperCount = 5;
                            LogMessage($"Папір поповнено. Залишок: {paperCount}");
                        }

                        paperCount--;
                        currentPaper = paperCount;
                    }
                    finally
                    {
                        paperMutex.ReleaseMutex();
                    }

                    Thread.CurrentThread.Priority = order.IsVip ? ThreadPriority.Highest : ThreadPriority.Normal;
                    string label = order.IsVip ? $"[VIP] Замовлення {order.Id}" : $"Замовлення {order.Id}";
                    LogMessage($"{Thread.CurrentThread.Name} друкує {label}. Паперу залишилося: {currentPaper}.");
                    Thread.Sleep(order.IsVip ? 800 : 1500);
                    LogMessage($"{label} надруковано.");
                    order.Completed.Set();
                }
                finally
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Normal;
                    printersSemaphore.Release();
                }
            }
        }

        private void CollectOrder(object? orderObj)
        {
            PrintOrder order = (PrintOrder)orderObj!;
            LogMessage($"Клієнт очікує на замовлення {order.Id}.");
            order.Completed.Wait();
            LogMessage($"Клієнт забрав замовлення {order.Id}.");

            if (Interlocked.Increment(ref collectedOrders) == orders.Count)
            {
                LogMessage("=== УСІ КЛІЄНТИ ЗАБРАЛИ ЗАМОВЛЕННЯ ===");
                BeginInvoke(new Action(() => btnStart.Enabled = true));
            }
        }

        private sealed class PrintOrder
        {
            public PrintOrder(int id, bool isVip)
            {
                Id = id;
                IsVip = isVip;
            }

            public int Id { get; }
            public bool IsVip { get; }
            public ManualResetEventSlim Completed { get; } = new(false);
        }

        private void LogMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(LogMessage), message);
            }
            else
            {
                listBoxLogs.Items.Add(message);
                listBoxLogs.TopIndex = listBoxLogs.Items.Count - 1;
            }
        }
    }
}