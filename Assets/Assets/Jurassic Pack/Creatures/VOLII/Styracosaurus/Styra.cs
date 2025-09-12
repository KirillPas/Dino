using UnityEngine;

public class Styra : Creature
{
	public Transform Spine0,Spine1,Spine2,Spine3,Spine4,Neck0,Neck1,Neck2,Neck3,Tail0,Tail1,Tail2,Tail3,Tail4,Tail5,Tail6,Tail7,Tail8, 
	Left_Arm0,Right_Arm0,Left_Arm1,Right_Arm1,Left_Hand,Right_Hand,Left_Hips,Right_Hips,Left_Leg,Right_Leg,Left_Foot,Right_Foot;
  public AudioClip Waterflush,Hit_jaw,Hit_head,Hit_tail,Medstep,Medsplash,Sniff2,Chew,Slip,Largestep,Largesplash,Idleherb,Styra1,Styra2,Styra3,Styra4;

	//*************************************************************************************************************************************************
	//Play sound
	void OnCollisionStay(Collision col)
	{
		int rndPainsnd=Random.Range(0, 4); AudioClip painSnd=null;
		switch (rndPainsnd) { case 0: painSnd=Styra1; break; case 1: painSnd=Styra2; break; case 2: painSnd=Styra3; break; case 3: painSnd=Styra4; break; }
		ManageCollision(col, source, painSnd, Hit_jaw, Hit_head, Hit_tail);
	}
	void PlaySound(string name, int time)
	{
		if(time==currframe && lastframe!=currframe)
		{
			switch (name)
			{
			case "Step": source[1].pitch=Random.Range(0.5f, 0.75f); 
				if(isInWater) source[1].PlayOneShot(Waterflush, Random.Range(0.25f, 0.5f));
				else if(isOnWater) source[1].PlayOneShot(Medsplash, Random.Range(0.25f, 0.5f));
				else if(isOnGround) source[1].PlayOneShot(Medstep, Random.Range(0.25f, 0.5f));
				lastframe=currframe; break;
			case "Slip": source[1].pitch=Random.Range(1.0f, 1.25f); source[1].PlayOneShot(isOnWater|isInWater?Largesplash:Slip, 0.5f);
				lastframe=currframe; break;
			case "Die": source[1].pitch=Random.Range(1.5f, 1.75f); source[1].PlayOneShot(isOnWater|isInWater?Largesplash:Largestep, 1.0f);
				lastframe=currframe; isDead=true; break;
			case "Sniff": source[0].pitch=Random.Range(1.2F, 1.5f); source[0].PlayOneShot(Sniff2, 0.25f);
				lastframe=currframe; break;
			case "Chew": source[0].pitch=Random.Range(1.0f, 1.25f); source[0].PlayOneShot(Chew, 0.5f);
				lastframe=currframe; break;
			case "Repose": source[0].pitch=Random.Range(1.5f, 1.75f); source[0].PlayOneShot(Idleherb, 0.25f);
				lastframe=currframe; break;
			case "Growl": int rnd=Random.Range (0, 4); source[0].pitch=Random.Range(1.0f, 1.25f);
				if(rnd==0)source[0].PlayOneShot(Styra1, 1.0f);
				else if(rnd==1)source[0].PlayOneShot(Styra2, 1.0f);
				else if(rnd==2)source[0].PlayOneShot(Styra3, 1.0f);
				else source[0].PlayOneShot(Styra4, 1.0f);
				lastframe=currframe; break;
			}
		}
	}
	
	//*************************************************************************************************************************************************
	// Add forces to the Rigidbody
	void FixedUpdate ()
	{
		StatusUpdate(); if(!isActive | animSpeed==0.0f) { body.Sleep(); return; }
		onReset=false; onAttack=false; isConstrained= false;

		if(useAI && health!=0) { AICore(1, 2, 3, 4, 5, 6, 7); }// CPU
		else if(health!=0) { GetUserInputs(1, 2, 3, 4, 5, 6, 7); }// Human
		else { anm.SetBool("Attack", false); anm.SetInteger ("Move", 0); anm.SetInteger ("Idle", -1); }//Dead

    //Set Y position
    if(isOnGround | isInWater | isOnWater)
    {
      if(!isOnGround && !isInWater) { body.linearDamping=1; body.angularDamping=1; } else { body.linearDamping=4; body.angularDamping=4; }
      ApplyYPos();
    } else ApplyGravity();
		
		
		//Stopped
		if(OnAnm.IsName("Styra|Idle1A") | OnAnm.IsName("Styra|Idle2A") | 
			OnAnm.IsName("Styra|Die1") | OnAnm.IsName("Styra|Die2"))
		{
      Move(Vector3.zero);
			if(OnAnm.IsName("Styra|Die1")) { onReset=true;  if(!isDead) { PlaySound("Growl", 2); PlaySound("Die", 12); } }
			else if(OnAnm.IsName("Styra|Die2")) { onReset=true; if(!isDead) { PlaySound("Growl", 2); PlaySound("Die", 10); } }
		}
		
		//Forward
		else if(OnAnm.IsName("Styra|Walk") | OnAnm.IsName("Styra|WalkGrowl") | OnAnm.IsName("Styra|Step1") | OnAnm.IsName("Styra|Step2") |
		   OnAnm.IsName("Styra|ToEatA") | OnAnm.IsName("Styra|ToEatC") | OnAnm.IsName("Styra|ToIdle2C"))
		{
			if(!(OnAnm.IsName("Styra|Step1")|(OnAnm.IsName("Styra|Step2")) && OnAnm.normalizedTime > 0.8))
			Move(transform.forward, 15);
			if(OnAnm.IsName("Styra|WalkGrowl")) { PlaySound("Growl", 1); PlaySound("Step", 6); PlaySound("Step", 13); }
			else if(OnAnm.IsName("Styra|Walk")) { PlaySound("Step", 6); PlaySound("Step", 13); }
			else PlaySound("Step", 9);
		}

		//Running
		else if(OnAnm.IsName("Styra|Run") | OnAnm.IsName("Styra|RunGrowl") |
					OnAnm.IsName("Styra|StepAtk1") | OnAnm.IsName("Styra|StepAtk2") | OnAnm.IsName("Styra|RunAtk1") | 
					(OnAnm.IsName("Styra|RunAtk2") && OnAnm.normalizedTime < 0.5))
		{
      roll=Mathf.Clamp(Mathf.Lerp(roll, spineX*10.0f, 0.1f), -20f, 20f);
			if((OnAnm.IsName("Styra|StepAtk1") | OnAnm.IsName("Styra|StepAtk2")) && OnAnm.normalizedTime>0.3)
      Move(Vector3.zero); else Move(transform.forward, 100);

			if(OnAnm.IsName("Styra|Run")) { PlaySound("Step", 3); PlaySound("Step", 6); }
			else if(OnAnm.IsName("Styra|RunGrowl")) { PlaySound("Growl", 2); PlaySound("Step", 3); PlaySound("Step", 6); }
			else if(OnAnm.IsName("Styra|RunAtk2")) { onAttack=true; PlaySound("Growl", 2); PlaySound("Slip", 6); }
			else { onAttack=true; PlaySound("Growl", 2); PlaySound("Step", 3); PlaySound("Step", 6); }
		}
		
		//Backward
		else if(OnAnm.IsName("Styra|Step1-") | OnAnm.IsName("Styra|Step2-") | OnAnm.IsName("Styra|ToSit1") |
		   	OnAnm.IsName("Styra|ToIdle1C") | OnAnm.IsName("Styra|ToIdle1D"))
		{
			Move(-transform.forward, 10);
			PlaySound("Step", 9);
		}

		//Strafe/Turn right
		else if(OnAnm.IsName("Styra|Strafe1-") | OnAnm.IsName("Styra|Strafe2+"))
		{
			Move(transform.right, 8);
			PlaySound("Step", 5); PlaySound("Step", 12);
		}

		//Strafe/Turn left
		else if(OnAnm.IsName("Styra|Strafe1+") | OnAnm.IsName("Styra|Strafe2-"))
		{
			Move(-transform.right, 8);
			PlaySound("Step", 5); PlaySound("Step", 12);
		}

		//Various
		else if(OnAnm.IsName("Styra|EatA")) PlaySound("Chew", 10);
		else if(OnAnm.IsName("Styra|EatB")) { PlaySound("Chew", 1); PlaySound("Chew", 4); PlaySound("Chew", 8); PlaySound("Chew", 12); }
		else if(OnAnm.IsName("Styra|EatC")) onReset=true;
		else if(OnAnm.IsName("Styra|ToSit")) isConstrained=true;
		else if(OnAnm.IsName("Styra|SitIdle")) isConstrained=true;
		else if(OnAnm.IsName("Styra|Sleep")) { onReset=true; isConstrained=true; PlaySound("Repose", 2); }
		else if(OnAnm.IsName("Styra|SitGrowl")) { isConstrained=true; PlaySound("Growl", 2); }
		else if(OnAnm.IsName("Styra|Idle1B")) { PlaySound("Growl", 2); PlaySound("Slip", 3); }
		else if(OnAnm.IsName("Styra|Idle1C")) { PlaySound("Growl", 2); PlaySound("Step", 5); PlaySound("Step", 6); PlaySound("Sniff", 9); }
		else if(OnAnm.IsName("Styra|Idle1D")) { PlaySound("Sniff", 1); PlaySound("Growl", 4); PlaySound("Step", 9); PlaySound("Step", 11); }
		else if(OnAnm.IsName("Styra|Idle2B")) { PlaySound("Growl", 2); PlaySound("Slip", 3); }
		else if(OnAnm.IsName("Styra|Idle2C")) { onReset=true; PlaySound("Sniff", 1); }
		else if(OnAnm.IsName("Styra|IdleAtk1") | OnAnm.IsName("Styra|IdleAtk2")) { onAttack=true; PlaySound("Growl", 2); PlaySound("Step", 5); PlaySound("Step", 6); }
		else if(OnAnm.IsName("Styra|Die1-")) { PlaySound("Growl", 3);  isDead=false; }
		else if(OnAnm.IsName("Styra|Die2-")) { PlaySound("Growl", 3);  isDead=false; }

    RotateBone(IkType.Quad, 55f);
	}

  //*************************************************************************************************************************************************
	// Bone rotation
	void LateUpdate()
	{
		if(!isActive) return; headPos=Head.GetChild(0).GetChild(0).position;
		float headZ =headY*headX/yaw_Max;
    Spine0.rotation*= Quaternion.Euler(0, 0, spineX);
    Spine1.rotation*= Quaternion.Euler(0, 0, spineX);
    Spine2.rotation*= Quaternion.Euler(0, 0, spineX);
    Spine3.rotation*= Quaternion.Euler(0, 0, spineX);
    Spine4.rotation*= Quaternion.Euler(0, 0, spineX);
		Neck0.rotation*= Quaternion.Euler(headY, headZ, headX);
		Neck1.rotation*= Quaternion.Euler(headY, headZ, headX);
		Neck2.rotation*= Quaternion.Euler(headY, headZ, headX);
		Neck3.rotation*= Quaternion.Euler(headY, headZ, headX);
		Head.rotation*= Quaternion.Euler(headY, headZ, headX);
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