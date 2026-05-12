# pip install bleak
import asyncio
from bleak import BleakScanner
from datetime import datetime


def _pick_name(device, adv):
    """Most BLE devices don't put their friendly name in the primary
    advertisement packet, so ``device.name`` is often ``None``. The
    AdvertisementData (when bleak >= 0.19 returns it) is the most reliable
    source. Fall back to a short suffix from the MAC so different unknown
    devices remain visually distinct in the UI."""
    name = None
    if adv is not None:
        name = getattr(adv, "local_name", None)
    if not name:
        name = getattr(device, "name", None)
    if name and str(name).strip():
        return str(name).strip()
    address = (getattr(device, "address", "") or "").replace(":", "").upper()
    tail = address[-4:] if len(address) >= 4 else address
    return "BLE Tag " + tail if tail else "Unknown"


async def scan_bluetooth_devices():
    print("Scanning for bluetooth devices...")
    scan_time = datetime.now().strftime("%Y-%m-%d %H:%M:%S")

    try:
        # `return_adv=True` (bleak >= 0.19) hands us the AdvertisementData,
        # which usually contains the local name even when device.name is empty.
        try:
            discovered = await BleakScanner.discover(timeout=8.0, return_adv=True)
            entries = [(addr, dev, adv) for addr, (dev, adv) in discovered.items()]
        except TypeError:
            # Older bleak: no return_adv support.
            devices = await BleakScanner.discover(timeout=8.0)
            entries = [(d.address, d, None) for d in devices]

        print(f"\nScan completed at {scan_time}")
        print(f"Found {len(entries)} devices:\n")

        for address, device, adv in entries:
            print(f"Device Name: {_pick_name(device, adv)}")
            print(f"MAC Address: {address}")

    except Exception as e:
        print(f"An error occurred: {str(e)}")


async def main():
    print("Starting Bluetooth scan...")
    await scan_bluetooth_devices()


if __name__ == "__main__":
    # Run the async function
    asyncio.run(main())
