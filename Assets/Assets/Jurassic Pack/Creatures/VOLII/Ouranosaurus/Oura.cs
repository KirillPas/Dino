using UnityEngine;

public class Oura : Creature
{
	public Transform Spine0,Spine1,Spine2,Spine3,Spine4,Neck0,Neck1,Neck2,Neck3,Tail0,Tail1,Tail2,Tail3,Tail4,Tail5,Tail6,Tail7,Tail8, 
	Left_Arm0,Right_Arm0,Left_Arm1,Right_Arm1,Left_Hand,Right_Hand,Left_Hips,Right_Hips,Left_Leg,Right_Leg,Left_Foot,Right_Foot;
  public AudioClip Waterflush,Hit_jaw,Hit_head,Hit_tail,Medstep,Medsplash,Sniff2,Chew,Largestep,Largesplash,Idleherb,Oura1,Oura2,Oura3;

	//*************************************************************************************************************************************************
	//Play sound
	void OnCollisionStay(Collision col)
	{
		int rndPainsnd=Random.Range(0, 3); AudioClip painSnd=null;
		switch (rndPainsnd) { case 0: painSnd=Oura1; break; case 1: painSnd=Oura2; break; case 2: painSnd=Oura3; break; }
		ManageCollision(col, source, painSnd, Hit_jaw, Hit_head, Hit_tail);
	}
	void PlaySound(string name, int time)
	{
		if(time==currframe && lastframe!=currframe)
		{
			switch (name)
			{
			case "Step": source[1].pitch=Random.Range(0.75f, 1.25f);
				if(isInWater) source[1].PlayOneShot(Waterflush, Random.Range(0.25f, 0.5f));
				else if(isOnWater) source[1].PlayOneShot(Medsplash, Random.Range(0.25f, 0.5f));
				else if(isOnGround) source[1].PlayOneShot(Medstep, Random.Range(0.25f, 0.5f));
				lastframe=currframe; break;
			case "Hit": source[1].pitch=Random.Range(1.0f, 1.25f); source[1].PlayOneShot(isOnWater|isInWater?Largesplash:Largestep, 1.0f);
				lastframe=currframe; break;
			case "Die": source[1].pitch=Random.Range(1.0f, 1.25f); source[1].PlayOneShot(isOnWater|isInWater?Largesplash:Largestep, 1.0f);
				lastframe=currframe; isDead=true; break;
			case "Sniff": source[0].pitch=Random.Range(1.0f, 1.25f); source[0].PlayOneShot(Sniff2, 0.5f);
				lastframe=currframe; break;
			case "Chew": source[0].pitch=Random.Range(1.0f, 1.25f); source[0].PlayOneShot(Chew, 0.5f);
				lastframe=currframe; break;
			case "Repose": source[0].pitch=Random.Range(1.0f, 1.25f); source[0].PlayOneShot(Idleherb, 0.25f);
				lastframe=currframe; break;
			case "Growl": int rnd1=Random.Range (0, 3); source[0].pitch=Random.Range(1.0f, 1.25f);
				if(rnd1==0)source[0].PlayOneShot(Oura1, 1.0f);
				else if(rnd1==1) source[0].PlayOneShot(Oura2, 1.0f);
				else source[0].PlayOneShot(Oura3, 1.0f);
				lastframe=currframe; break;
			}
		}
	}
	
	//*************************************************************************************************************************************************
	// Add forces to the Rigidbody
	void FixedUpdate ()
	{
		StatusUpdate(); if(!isActive | animSpeed==0.0f) { body.Sleep(); return; }
		onReset=false; isConstrained= false;

		if(useAI && health!=0) { AICore(1, 2, 3, 4, 5, 6, 7); }// CPU   
		else if(health!=0) { GetUserInputs(1, 2, 3, 4, 5, 6, 7, 4); }// Human
		else { anm.SetInteger ("Move", 0); anm.SetInteger ("Idle", -1); }//Dead

    //Set Y position
    if(isOnGround | isInWater | isOnWater)
    {
      if(!isOnGround && !isInWater) { body.linearDamping=1; body.angularDamping=1; } else { body.linearDamping=4; body.angularDamping=4; }
      ApplyYPos();
    } else ApplyGravity();
		
		//Stopped
		if(OnAnm.IsName("Oura|Idle1A") | OnAnm.IsName("Oura|Idle2A") |
			 OnAnm.IsName("Oura|Die1") | OnAnm.IsName("Oura|Die2"))
		{
      Move(Vector3.zero);
			if(OnAnm.IsName("Oura|Die1")) { onReset=true; if(!isDead) { PlaySound("Growl", 2); PlaySound("Die", 12); } }
			else if(OnAnm.IsName("Oura|Die2")) { onReset=true; if(!isDead) { PlaySound("Growl", 2); PlaySound("Die", 10); } }
		}
		
		//Forward
		else if(OnAnm.IsName("Oura|Walk") | OnAnm.IsName("Oura|WalkGrowl") | OnAnm.IsName("Oura|Step1") | OnAnm.IsName("Oura|Step2") |
		   OnAnm.IsName("Oura|ToEatA") | OnAnm.IsName("Oura|ToEatC") | OnAnm.IsName("Oura|ToIdle1D"))
		{
      if(!(OnAnm.IsName("Oura|Step1")|(OnAnm.IsName("Oura|Step2")) && OnAnm.normalizedTime > 0.8))
			Move(transform.forward, 15);
			if(OnAnm.IsName("Oura|WalkGrowl")) { PlaySound("Growl", 1); PlaySound("Step", 6); PlaySound("Step", 13); }
			else if(OnAnm.IsName("Oura|Walk")) { PlaySound("Step", 6); PlaySound("Step", 13); }
			else PlaySound("Step", 9);
		}

		//Running
		else if(OnAnm.IsName("Oura|Run") | OnAnm.IsName("Oura|RunGrowl"))
		{
      roll=Mathf.Clamp(Mathf.Lerp(roll, spineX*10.0f, ang_T), -20f, 20f);
			Move(transform.forward, 80);
			if(OnAnm.IsName("Oura|RunGrowl")) { PlaySound("Growl", 2); PlaySound("Step", 5); PlaySound("Step", 12); }
			else { PlaySound("Step", 5); PlaySound("Step", 12);}
		}
		
		//Backward
		else if(OnAnm.IsName("Oura|Step1-") | OnAnm.IsName("Oura|Step2-") | OnAnm.IsName("Oura|ToSit1"))
		{
			Move(-transform.forward, 15);
			PlaySound("Step", 9);
		}
		
		//Strafe/Turn right
		else if(OnAnm.IsName("Oura|Strafe1+") | OnAnm.IsName("Oura|Strafe2+"))
		{
			Move(transform.right, 8);
			PlaySound("Step", 5); PlaySound("Step", 12);
		}
		
		//Strafe/Turn left
		else if(OnAnm.IsName("Oura|Strafe1-") |OnAnm.IsName("Oura|Strafe2-"))
		{
			Move(-transform.right, 8);
			PlaySound("Step", 5); PlaySound("Step", 12);
		}

		//Various
		else if(OnAnm.IsName("Oura|EatA")) PlaySound("Chew", 10);
		else if(OnAnm.IsName("Oura|EatB")) { PlaySound("Chew", 1); PlaySound("Chew", 4); PlaySound("Chew", 8); PlaySound("Chew", 12); }
		else if(OnAnm.IsName("Oura|EatC")) onReset=true;
		else if(OnAnm.IsName("Oura|ToSit")) isConstrained=true;
		else if(OnAnm.IsName("Oura|SitIdle")) isConstrained=true;
		else if(OnAnm.IsName("Oura|Sleep")) { onReset=true; isConstrained=true; PlaySound("Repose", 2); }
		else if(OnAnm.IsName("Oura|SitGrowl")) { isConstrained=true; PlaySound("Growl", 2); }
		else if(OnAnm.IsName("Oura|Idle1B")) PlaySound("Growl", 2);
		else if(OnAnm.IsName("Oura|Idle1C")) PlaySound("Growl", 1);
		else if(OnAnm.IsName("Oura|Idle1D")) { onReset=true; PlaySound("Sniff", 1); }
		else if(OnAnm.IsName("Oura|Idle2B")) PlaySound("Growl", 2);
		else if(OnAnm.IsName("Oura|Idle2C")) PlaySound("Growl", 2);
		else if(OnAnm.IsName("Oura|ToRise1") | OnAnm.IsName("Oura|ToRise2")) {  onReset=true; PlaySound("Sniff", 3); PlaySound("Growl", 1); }
		else if(OnAnm.IsName("Oura|ToRise1-")| OnAnm.IsName("Oura|ToRise2-")) PlaySound("Hit", 7);
    else if(OnAnm.IsName("Oura|Rise1Idle") | OnAnm.IsName("Oura|Rise2Idle")) onReset=true;
		else if(OnAnm.IsName("Oura|Rise1Growl") | OnAnm.IsName("Oura|Rise2Growl"))  { onReset=true; PlaySound("Growl", 1); }
		else if(OnAnm.IsName("Oura|Die1-") | OnAnm.IsName("Oura|Die2-")) { PlaySound("Growl", 3);  isDead=false; }

    RotateBone(IkType.Quad, 50f);
	}
	
  //*************************************************************************************************************************************************
	// Bone rotation
	void LateUpdate()
	{
		if(!isActive) return; headPos=Head.GetChild(0).GetChild(0).position;
    float headZ =-headY*headX/yaw_Max;
    Spine0.rotation*= Quaternion.Euler(0, 0, spineX);
		Spine1.rotation*= Quaternion.Euler(0, 0, spineX);
		Spine2.rotation*= Quaternion.Euler(0, 0, spineX);
		Spine3.rotation*= Quaternion.Euler(0, 0, spineX);
    Spine4.rotation*= Quaternion.Euler(0, 0, spineX);
		Neck0.rotation*= Quaternion.Euler(headY, headX, headZ);
		Neck1.rotation*= Quaternion.Euler(headY, headX, headZ);
		Neck2.rotation*= Quaternion.Euler(headY, headX, headZ);
		Neck3.rotation*= Quaternion.Euler(headY, headX, headZ);
		Head.rotation*= Quaternion.Euler(headY, headX, headZ);
		Tail0.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail1.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail2.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail3.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail4.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail5.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail6.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail7.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail8.rotation*= Quaternion.Euler(0, 0, -spineX);
		Right_Hips.rotation*= Quaternion.Euler(-roll, 0, 0);
		Left_Hips.rotation*= Quaternion.Euler(-roll, 0, 0);
		Right_Arm0.rotation*= Quaternion.Euler(-roll, 0, 0);
		Left_Arm1.rotation*= Quaternion.Euler(0, roll, 0);
    if(!isDead) Head.GetChild(0).transform.rotation*=Quaternion.Euler(-lastHit, 0, 0);
		//Check for ground layer
		GetGroundPos(IkType.Quad, Right_Hips, Right_Leg, Right_Foot, Left_Hips, Left_Leg, Left_Foot, Right_Arm0, Right_Arm1, Right_Hand, Left_Arm0, Left_Arm1, Left_Hand, -0.5f*size);
	}
}