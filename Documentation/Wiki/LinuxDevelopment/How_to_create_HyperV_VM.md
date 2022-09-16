# How to Set Up a Linux VM using HyperV

* `Hyper-V Settings` -> Make sure that the locations where to store VMs and VHDs are pointing to drive with plenty of free space. SSD is strongly advised. 
* `Virtual Switch Manager` -> Create a new External switch and call it 'External' 
* `Quick Create` -> Select Ubuntu 20.04.x or newer (distros other than Ubuntu could also work, but the [VM prep steps](./How_to_prep_VM.md) might be different)
* Once the VM is created, but before connecting to it, open its settings: 
* Update memory, processor count, select 'External' Network Adapter. 
* Uncheck 'Use automatic checkpoints' 
* `Action` -> `Edit Disk` -> `Select downloaded VHD` -> `Expand` -> Set to 200GB at minimum 
* The previous step can be repeated if more disk space is needed. 
* Do not select the "Log me in automatically" option during set up as this will break enhanced session. 
* Follow the steps below to update the volume size: 
    * sudo apt install gparted && sudo gparted
    * In the GUI, select the current drive at the top by clicking on it and then click on the expand button ("->"). Assign the entire Total space to the current drive. 
    * Then click on the green check mark to expand the drive. 
    * Then run sudo reboot to restart the VM for the changes to take effect. 