using ApplicationCore.Interfaces;
using ApplicationCore.Entities.OrderAggregate;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace ApplicationCore.Services
{
    public class OrderService : IOrderService
    {
        private readonly IAsyncRepository<Order> _orderRepository;
        private readonly IAsyncRepository<Basket> _basketRepository;
        private readonly IAsyncRepository<CatalogItem> _itemRepository;

        private string path = null;

        public OrderService(IAsyncRepository<Basket> basketRepository,
            IAsyncRepository<CatalogItem> itemRepository,
            IAsyncRepository<Order> orderRepository)
        {
            _orderRepository = orderRepository;
            _basketRepository = basketRepository;
            _itemRepository = itemRepository;

            // SMB configuration
            string vcapServices = System.Environment.GetEnvironmentVariable("VCAP_SERVICES");
            // if we are on the cloud and DB service was bound successfully...
            if (vcapServices != null)
            {
                dynamic json = JsonConvert.DeserializeObject(vcapServices);
                foreach (dynamic obj in json.Children())
                {
                    if (((string)obj.Name).ToLowerInvariant().Contains("smbvolume"))
                    {
                        dynamic volume_mounts = (((JProperty)obj).Value[0] as dynamic).volume_mounts;
                        dynamic mount_path = (volume_mounts[0] as dynamic).container_dir;
                        Console.Write(mount_path);
                        path = mount_path;

                        break;
                    }
                }
            }
        }

        private async Task writeOrderInvoice(List<OrderItem> items, Address shippingAddress)
        {
            if (path != null)
            {
                using (var fileStream = System.IO.File.Create(path + '/' + System.DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".csv"))
                using (var fileWriter = new System.IO.StreamWriter(fileStream))
                {
                    fileWriter.WriteLine("\"Product ID\",\"Product Name\",\"Unit Price\",\"Units\",\"Subtotal\"");
                    foreach (var item in items)
                    {
                        fileWriter.Write("\"");
                        fileWriter.Write(item.Id);
                        fileWriter.Write("\",\"");
                        fileWriter.Write(item.ItemOrdered.ProductName);
                        fileWriter.Write("\",\"");
                        fileWriter.Write(item.UnitPrice);
                        fileWriter.Write("\",\"");
                        fileWriter.Write(item.Units);
                        fileWriter.Write("\",\"");
                        fileWriter.Write(item.UnitPrice * item.Units);
                        fileWriter.WriteLine("\"");
                    }

                    fileWriter.WriteLine();
                    fileWriter.Write("\"");
                    fileWriter.Write(shippingAddress.Street);
                    fileWriter.Write("\",\"");
                    fileWriter.Write(shippingAddress.City);
                    fileWriter.Write("\",\"");
                    fileWriter.Write(shippingAddress.State);
                    fileWriter.Write("\",\"");
                    fileWriter.Write(shippingAddress.Country);
                    fileWriter.Write("\",\"");
                    fileWriter.Write(shippingAddress.ZipCode);
                    fileWriter.WriteLine("\"");
                }
            }
        }

        public async Task CreateOrderAsync(int basketId, Address shippingAddress)
        {
            var basket = await _basketRepository.GetByIdAsync(basketId);
            Guard.Against.NullBasket(basketId, basket);
            var items = new List<OrderItem>();
            foreach (var item in basket.Items)
            {
                var catalogItem = await _itemRepository.GetByIdAsync(item.CatalogItemId);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
                var orderItem = new OrderItem(itemOrdered, item.UnitPrice, item.Quantity);
                items.Add(orderItem);
            }
            var order = new Order(basket.BuyerId, shippingAddress, items);

            await writeOrderInvoice(items, shippingAddress);

            await _orderRepository.AddAsync(order);
        }
    }
}
