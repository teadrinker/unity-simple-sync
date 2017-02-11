# unity-simple-sync
Synchronize scene objects between your unity app and the editor using a PHP script

**When developing for mobile VR, getting feedback for a small change can take several minutes and involve moving the phone in and out of the headset and plug it into the computer USB. This has been annoying me for a some time so I decided to spend a weekend trying to get a working solution** (took almost two weekends, synchronization is tricky...)

#Usage
You simply tag a few objects in your scene with the tag "spSynced", and add a script which handles the rest. It supports multiple running instances of the app and multiple editors (sync code makes no distinction). It takes the local position, rotation and scale for the objects, put it into a json file, and sends it to the server via http. Changes are pushed to clients using a long-held HTTP request (or "comet").

#Install
 * **Start the server**, if you don't already have one, I recommend XAMPP Lite, it's a small pre-configured server that you can run locally. There's a portable version that don't even need to be installed, just extract the files and run xampp_start
 * **Move the dir spSync (in ServerRoot) to XAMPP/htdocs**
 * **You should now be test it!** Start unity, open the SyncTest.unity, build and run it. Then hit play in the editor (so that you are running both the compiled version and also within the editor. Clicking and draging in either window should update the other as well.
 * **Remember to change the server secret, both in server_job.php and in your unity scene!**
 * **Change URL** To be able to access the local server from your mobile device, you need to change http://localhost/spSync (on the spSync GameObject in the unity test scene) to something like http://192.168.1.100/spSync (to find your local ip on windows, write "ipconfig" in the command line prompt)
 
 #This is a hack, use at your own risk... 
