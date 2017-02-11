<?php

//                 - server_job.php -
//                 Martin Eklund 2017
//
//   !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
//   !!!                                         !!!
//   !!!          Use at your own risk!          !!!
//   !!!                                         !!!
//   !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
//
// This is a hack to do basic filehandling over LAN or internet.
// You supply a list of server jobs in json such as:
//
//    server_job.php?hashJobs=[{"cmd":"get","relPath":"myfile.txt"}]&hash=72858d3f50eb016005747254715f5d3d45052673ee0df7de0523f32fa144459f
//
// The commands are:
//   * "set"     write to a file
//   * "get"     read from file
//   * "getInfo" get file size and time
//   * "mkdir"   create a directory
//
// For "get", there are two options "cometHeader" and "cometId",
// these can be used to create a "server push"/hook, the server will
// will wait until the file changes and then return it, if the file doesn't
// change in $kCometTimeOut seconds, it will return "comet timeout"
//
// For "set", there are two options "assertHeader" and "assertVersionId"
// that will ensure that data will only be written if the server data
// was not already updated by someone else. Returns "ver_err" on fail
//
// Using these options, it is possible to get *reasonably* robust
// and responsive behaviour doing the main versioning on the client.
// It is only designed for a small number of simultanous users, and 
// very small document sizes (everything is rewritten for each change)

$serverSecret = 'ReplaceWithYourSecret';

$HashingAlg = 'sha256';			
$HashingSalt =	'HashingSalt_' . $serverSecret;




// There are 2 alternatives to hashJobs/hash, 
//
//   * plainJobs - For test/debug (localhost only!), no security
//   * jobs      - AES Encrypted 
//
// The encrypted version has not been tested for a while, might be broken...
// To use it, you need: http://www.movable-type.co.uk/scripts/aes-php.html 
$AesBits = 128;			
$SymmetricWriteKey = 'SymmetricWriteKey_' . $serverSecret;
$SymmetricReturnKey = 'SymmetricReturnKey_' . $serverSecret;




$serverLagMS = 210;            // don't kill the server, file-change notifications are
                               // implemented by polling and flushing the file cache

$serverLagAsLocalhostMS = 30;  // localhost can work harder...

$kCometTimeOut = 25;
//set_time_limit($kCometTimeOut + 10); // some servers do not allow this

error_reporting( error_reporting() & ~E_NOTICE );


ini_set('memory_limit', '-1'); // avoid memory limitation

$serverName = $_SERVER['HTTP_HOST'];
if(!$serverName) {
	$serverName = $_SERVER['SERVER_NAME'];
}  

$kPrivateServer = $serverName == 'localhost'; // || substr($serverName,0,8) == "192.168.";

if($kPrivateServer) {
	$serverLagMS = $serverLagAsLocalhostMS;
}

$out_dir = dirname(__FILE__);

$jsonStr = '[]';
$hasPostHashJob = $_POST['hashJobs'] && $_POST['hash'];
$hasGetHashJob = $_GET['hashJobs'] && $_GET['hash'];
if($hasPostHashJob || $hasGetHashJob) {

	if(hash($HashingAlg, ($hasPostHashJob ? $_POST['hashJobs'] : $_GET['hashJobs']) . $HashingSalt) == ($hasPostHashJob ? $_POST['hash'] : $_GET['hash'])) {
		$jsonStr = $hasPostHashJob ? $_POST['hashJobs'] : $_GET['hashJobs'];
	} else {
		die('fail:incorrect hash');
	}

	function serverJobsReturnEncrypt($text) {
		return $text;
	}

} else if($kPrivateServer && ($_POST['plainJobs'] || $_GET['plainJobs'])) {
	$jsonStr = $_POST['plainJobs'] ? $_POST['plainJobs'] : $_GET['plainJobs'];

	function serverJobsReturnEncrypt($text) {
		return $text;
	}

} else if($_POST['jobs']) {
	$encryptedJsonStr = $_POST['jobs'];

	require 'aes.php';     // AES PHP implementation

	function serverJobsDecrypt($text) {
		// Decrypt using the "public-write-key" later ?

		return AesCtr::decrypt($text, $SymmetricWriteKey, 128);
	}

	function serverJobsReturnEncrypt($text) {
		// Encrypt using the "public-return-key" later ?

		return AesCtr::encrypt($text, $SymmetricReturnKey, 128);
	//	return $text;
	}

	$jsonStr = serverJobsDecrypt($encryptedJsonStr);
} else {
	die('Error, missing jobs');
}


$jobs = json_decode($jsonStr, true);

//echo($jsonStr);  print('<br>');
//echo($jobs);     print('<br>');
//echo(serialize($jobs));  print('<br>');

if($jobs == NULL) die("Error, no valid jobs, " . json_last_error_msg());



$outList = array();

foreach($jobs as $job) {
	$out = 'internal error 666';	
	$cmd = $job['cmd'];

	if($cmd == 'set') {
		$filename = $out_dir . '/' . $job['relPath'];
		$data = $job['data'];
		$versionFail = false;

		$header = $job['assertHeader'];
		if($header) {
			if(file_exists($filename)) {
				$fileHandle = fopen($filename, "r");
				$headerOfFile = fread($fileHandle, strlen($header)); 
				fclose($fileHandle);
				if($header != $headerOfFile) {
					$versionFail = true;
				}
			} else {
				$versionFail = true;
			}
		}

		$fileversionId = $job['assertVersionId'];
		if($fileversionId) {
			if($fileversionId != filemtime($filename)) {
				$versionFail = true;
			//	$out = // todo, actually load file instead and return?
						// maybe not load file... client might already have a hook/comet on the data, just return update fail
			}
		}		

		if(!$versionFail) {

			$f = @fopen($filename, "wb");

			if ($f == false) 
			    die("Fatal error! Could not write file {$filename}");
			    

			if ($job['bin']) {
				$data = base64_decode($data);
			}

			fwrite($f, $data ); 

			fclose($f);

			$out = "success";
		} else {
			$out = "ver_err";			
		}

		if($job['includeInfo']) {
			clearstatcache(); // is this a bug? writing a file does not seem to clear the cache for filetime?
			$out = $out . ":" . str_pad(filesize($filename),11," ") . str_pad(filemtime($filename),11," ");
		}

	} else if($cmd == 'get' || $cmd == 'getInfo') {
		$filename = $out_dir . '/' . $job['relPath'];

		if($job['relPath'] && file_exists($filename)) {

			$header = $job['cometHeader'];
			if($header) {
				if(file_exists($filename)) {
					$headerCheckingTimePassedMS = 0;
					$fileChanged = false;
					$fileversionId = 0;
					while($headerCheckingTimePassedMS < 1000) {
						clearstatcache();
						$fileversionId = filemtime($filename);
						$fileHandle = fopen($filename, "r");
						$headerOfFile = fread($fileHandle, strlen($header)); 
						fclose($fileHandle);
						if($header != $headerOfFile) {
							$fileChanged = true;
							break;
						}
						$headerCheckingTimePassedMS += $serverLagMS;
						usleep($serverLagMS * 1000); // sleep to unload the CPU
					}
					if(!$fileChanged) {
						$start_time = time();
						$currentId = filemtime($filename);
						while ($currentId == $fileversionId) // check if the data file has been modified
						{
							if ((time() - $start_time) > $kCometTimeOut) {
								die('comet timeout');
							}	
						  usleep($serverLagMS * 1000); // sleep to unload the CPU
						  clearstatcache();
						  $currentId = filemtime($filename);
						}						
					}
				}
			}


			// Note that cometId only works reliably as long as you can make
			// sure that data is not pushed to server faster than once per second
			// since this is the precision of filetime (which is used as versioning id)
			$fileversionId = $job['cometId'];
			if($fileversionId) {
				$start_time = time();
				$currentId = filemtime($filename);
				while ($currentId == $fileversionId) // check if the data file has been modified
				{
					if ((time() - $start_time) > $kCometTimeOut) {
						die('comet timeout');
					}	
				  usleep($serverLagMS * 1000); // sleep to unload the CPU
				  clearstatcache();
				  $currentId = filemtime($filename);
				}

			}

			$returnData = $cmd != 'getInfo';
			$returnInfo = $cmd == 'getInfo' || $job['includeInfo'];

			$out = 'success:';

			if($returnInfo) {
				$out = $out . str_pad(filesize($filename),11," ") . str_pad(filemtime($filename),11," ");
			}

			if($returnData) {
		    	$fOffset = $job['offset'];
			    $fMaxLen = $job['maxLen'];
				$binary = $job['bin'];
			    if ($binary) {
			    	$fsize = filesize($filename);
			    	if($fMaxLen && (int)($fMaxLen) < $fsize) {
			    		$fsize = $fMaxLen;
			    	}
			    	$fileHandle = fopen($filename, "r");
			    	if($fOffset) fseek($fileHandle, $fOffset, SEEK_SET); 
			        $binaryData = fread($fileHandle, $fsize);
			        fclose($fileHandle);
					$out = $out . ':' . base64_encode($binaryData);
			    } else {

					if(!$fMaxLen && !$fOffset)
						$out = $out . file_get_contents($filename);
					else if($fMaxLen)
						$out = $out . file_get_contents($filename, NULL, NULL, $fOffset ? $fOffset : 0, $fMaxLen);
					else
						$out = $out . file_get_contents($filename, NULL, NULL, $fOffset ? $fOffset : 0);
			    }
			}

		} else {
			$out = "no such file " . $job['relPath'];
		}

	} else if($cmd == 'mkdir') {
		$filename = $out_dir . '/' . $job['relPath'];

		if (!file_exists($filename)) {
		    if (!mkdir($filename, 0777, true)) {
	   			$out = "Error! mkdir failed:" . $job['relPath'];	
		    } else {
	   			$out = "success";		
		    }
		} else {
			$out = $job['relPath'] . " already exists";		
		}

/*	} else if($cmd == 'dir') {
		// todo?
*/
	} else {
		die("ERROR! no such command:" . $cmd);
	}
	array_push($outList, $out);
}

if(count($outList) == 1) { // hmm, annoying special case
	print($outList[0]);
} else {
	print("success:" . serverJobsReturnEncrypt(json_encode($outList)) );
}

php?>